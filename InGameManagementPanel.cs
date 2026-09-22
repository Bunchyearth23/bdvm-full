using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BDVM.Adapters;
using BDVM.Management;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BDVM;

// The view owns its detached presentation. Drawing never captures the world or sends commands.
internal sealed class InGameManagementPanel
{
    private readonly Func<Task<InGameManagementView>?> read;
    private readonly Func<string, string> execute;
    private readonly Func<string, string?> poll;
    private readonly Func<string> context;
    private readonly ConcurrentQueue<Action> completions = new ConcurrentQueue<Action>();
    private InGameManagementView? view, filteredView;
    private string filteredArea = "", filteredSearch = "";
    private string[] visibleRows = System.Array.Empty<string>();
    private ManagementActionDescriptor[] visibleActions = System.Array.Empty<ManagementActionDescriptor>();
    private bool busy, waiting, refreshRequested, active;
    private int generation, page, actionPage;
    private string world = "", area = "wallets", filter = "", expanded = "";
    private readonly Dictionary<string, string> values = new Dictionary<string, string>();
    private readonly Dictionary<string, HashSet<string>> selections = new Dictionary<string, HashSet<string>>();
    private JObject? confirmation, pending;
    private string confirmationText = "", pendingTitle = "";
    private bool uncertain;
    private DateTime nextPoll;
    private GUIStyle? heading, wrap;
    public string Status { get; private set; } = "Open Management to view your activity.";

    public InGameManagementPanel(Func<Task<InGameManagementView>?> read, Func<string, string> execute,
        Func<string, string?> poll, Func<string> context)
    { this.read = read; this.execute = execute; this.poll = poll; this.context = context; }

    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        if (active) refreshRequested = true;
    }

    public void Tick()
    {
        var current = context();
        if (world != current)
        {
            world = current; generation++; view = null; waiting = false; refreshRequested = active;
            pending = null; confirmation = null; expanded = ""; values.Clear(); selections.Clear();
            Status = "Load a career, then open Management.";
        }
        while (completions.TryDequeue(out var complete)) complete();
        if (!active) return;
        if (pending != null && DateTime.UtcNow >= nextPoll)
        {
            nextPoll = DateTime.UtcNow.AddMilliseconds(250);
            try
            {
                var result = poll((string)pending["correlationId"]!);
                if (result != null) AcceptResult(result);
            }
            catch (Exception ex) { uncertain = true; Status = "Response unavailable: " + ex.Message; }
        }
        if (busy || (!waiting && !refreshRequested)) return;
        try
        {
            var task = read();
            refreshRequested = false;
            if (task == null) { waiting = true; if (view == null) Status = "Waiting for the host..."; return; }
            waiting = false; busy = true; var requestedGeneration = generation;
            task.ContinueWith(completed => {
                var failure = completed.IsFaulted ? completed.Exception?.GetBaseException().Message :
                    completed.IsCanceled ? "Refresh cancelled." : null;
                var result = failure == null ? completed.GetAwaiter().GetResult() : null;
                completions.Enqueue(() => {
                    busy = false;
                    if (requestedGeneration != generation || !active) return;
                    if (failure != null) { Status = "Refresh failed: " + failure; return; }
                    var firstView = view == null; view = result;
                    if (!view!.Areas.Contains(area)) area = "wallets";
                    if (pending == null && (firstView || Status == "Refreshing...")) Status = "Up to date. Refresh to see changes made by other players.";
                });
            }, TaskScheduler.Default);
        }
        catch (Exception ex) { waiting = false; refreshRequested = false; Status = "Unavailable: " + ex.Message; }
    }

    public void Draw()
    {
        if (heading == null) { heading = new GUIStyle(GUI.skin.label) { fontSize = 17 }; wrap = new GUIStyle(GUI.skin.label) { wordWrap = true }; }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Management", heading);
        var enabled = GUI.enabled; GUI.enabled = !busy && !waiting;
        if (GUILayout.Button(busy || waiting ? "Refreshing..." : "Refresh", GUILayout.Width(120))) { refreshRequested = true; Status = "Refreshing..."; }
        GUI.enabled = enabled; GUILayout.EndHorizontal();
        if (view == null) { GUILayout.Label(Status, wrap); return; }
        GUILayout.Box(view.WalletSummary, wrap);
        var current = System.Array.IndexOf(view.Areas, area);
        var next = GUILayout.SelectionGrid(Math.Max(0, current), view.AreaTitles, 5);
        if (next != current) { area = view.Areas[next]; page = actionPage = 0; filter = ""; expanded = ""; values.Clear(); selections.Clear(); }
        GUILayout.BeginHorizontal(); GUILayout.Label("Search", GUILayout.Width(55));
        var search = GUILayout.TextField(filter, 120);
        if (search != filter) { filter = search; page = actionPage = 0; }
        GUILayout.EndHorizontal();
        if (confirmation != null)
        {
            GUILayout.BeginVertical(GUI.skin.box); GUILayout.Label(confirmationText, wrap);
            GUILayout.Label("Review the selected options and amount before confirming.", wrap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm")) { var command = confirmation; confirmation = null; Send(command); }
            if (GUILayout.Button("Go back")) confirmation = null;
            GUILayout.EndHorizontal(); GUILayout.EndVertical();
        }
        if (pending != null)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(uncertain ? "The outcome is not confirmed. Check the same request before starting another action." : "Waiting for the host to confirm: " + pendingTitle, wrap);
            if (uncertain && GUILayout.Button("Check / retry the same request")) Send(pending);
            GUILayout.EndVertical();
        }
        if (!ReferenceEquals(filteredView, view) || filteredArea != area || filteredSearch != filter)
        {
            filteredView = view; filteredArea = area; filteredSearch = filter;
            visibleRows = view.Rows.TryGetValue(area, out var found) ? found.Where(Matches).ToArray() : System.Array.Empty<string>();
            visibleActions = view.Actions.Where(a => a.Area == area && Matches(a.Label)).ToArray();
        }
        var rows = visibleRows;
        GUILayout.Label(InGameManagementModel.Title(area) + " (" + rows.Length + ")", heading);
        if (rows.Length == 0) GUILayout.Label("Nothing to display here yet.", wrap);
        page = Pager(page, rows.Length, 8);
        foreach (var row in rows.Skip(page * 8).Take(8)) GUILayout.Box(row, wrap);
        if (area == "deliveries") GUILayout.Label("Place purchased vehicles with the BDVM delivery radio, or choose an available delivery track below.", wrap);
        if (area == "contracts") GUILayout.Label("For a new transport, tag your available wagons in Fleet first. Matching cargo already aboard can be used. Loading and unloading remain physical operations.", wrap);
        var actions = visibleActions;
        GUILayout.Label("Actions (" + actions.Length + ")", heading);
        if (actions.Length == 0) GUILayout.Label("No eligible action. Check your fleet, company permissions or refresh.", wrap);
        actionPage = Pager(actionPage, actions.Length, 10);
        GUI.enabled = enabled && pending == null && confirmation == null && !busy && !waiting;
        if (area == "wallets" && view.CanGrantHostMoney && GUILayout.Button("Magique : +100 000 $"))
        {
            pendingTitle = "+100 000 $ dans votre portefeuille";
            Send(new JObject { ["action"] = "host.grant-money", ["correlationId"] = Guid.NewGuid().ToString("N") });
        }
        foreach (var action in actions.Skip(actionPage * 10).Take(10)) DrawAction(action);
        GUI.enabled = enabled;
    }

    private bool Matches(string text) => filter.Length == 0 || text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    private int Pager(int index, int count, int size)
    {
        var pages = Math.Max(1, (count + size - 1) / size); index = Math.Min(index, pages - 1);
        if (pages <= 1) return index;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous", GUILayout.Width(100))) index = Math.Max(0, index - 1);
        GUILayout.Label((index + 1) + " / " + pages, GUILayout.Width(80));
        if (GUILayout.Button("Next", GUILayout.Width(100))) index = Math.Min(pages - 1, index + 1);
        GUILayout.EndHorizontal(); return index;
    }

    private void DrawAction(ManagementActionDescriptor action)
    {
        var key = view!.ActionKeys[action];
        if (GUILayout.Button((key == expanded ? "- " : "+ ") + action.Label))
        {
            if (expanded == key) expanded = "";
            else { expanded = key; values.Clear(); selections.Clear(); }
        }
        if (expanded != key) return;
        GUILayout.BeginVertical(GUI.skin.box);
        foreach (var field in action.Fields)
        {
            if (!values.ContainsKey(field.Name)) values[field.Name] = field.Value.Length > 0 ? field.Value : field.Kind == "select" ? field.Options.FirstOrDefault() ?? "" : "";
            GUILayout.Label(field.Label, wrap);
            if (field.Kind == "checkbox") values[field.Name] = GUILayout.Toggle(values[field.Name] == "true", "Yes") ? "true" : "false";
            else if (field.Kind == "select" || field.Kind == "multiselect")
            {
                if (field.Options.Count == 0) GUILayout.Label("No eligible option available.", wrap);
                if (!selections.TryGetValue(field.Name, out var chosen)) selections[field.Name] = chosen = new HashSet<string>();
                // Bounded option pages, retaining selection across pages.
                var pageKey = "page:" + field.Name;
                if (!values.ContainsKey(pageKey)) values[pageKey] = "0";
                int.TryParse(values[pageKey], out var choicePage);
                values[pageKey] = (choicePage = Pager(choicePage, field.Options.Count, 12)).ToString();
                foreach (var option in field.Options.Skip(choicePage * 12).Take(12))
                {
                    var label = field.OptionLabels.TryGetValue(option, out var friendly) ? friendly : InGameManagementModel.Humanize(option);
                    var before = field.Kind == "select" ? values[field.Name] == option : chosen.Contains(option);
                    var after = GUILayout.Toggle(before, label);
                    if (field.Kind == "select") { if (after && !before) values[field.Name] = option; }
                    else if (after) chosen.Add(option); else chosen.Remove(option);
                }
                if (field.Kind == "multiselect") GUILayout.Label(chosen.Count + " selected");
            }
            else values[field.Name] = GUILayout.TextField(values[field.Name], 256);
        }
        if (GUILayout.Button(action.Label))
        {
            try
            {
                var command = InGameManagementModel.BuildCommand(action, values, selections);
                pendingTitle = action.Label;
                if (action.Confirmation.Length > 0)
                {
                    confirmation = command;
                    confirmationText = action.Confirmation + "\n" + string.Join("\n", action.Fields.Select(field =>
                        field.Label + ": " + (field.Kind == "multiselect" && selections.TryGetValue(field.Name, out var ids)
                            ? string.Join(", ", ids.Select(id => field.OptionLabels.TryGetValue(id, out var label) ? label : id))
                            : values.TryGetValue(field.Name, out var value) ? (field.OptionLabels.TryGetValue(value, out var label) ? label : value) : "")));
                }
                else Send(command);
            }
            catch (Exception ex) { Status = "Please check: " + ex.Message; }
        }
        GUILayout.EndVertical();
    }

    private void Send(JObject command)
    {
        pending = command; uncertain = false; nextPoll = DateTime.UtcNow;
        try { AcceptResult(execute(command.ToString(Formatting.None))); }
        catch (Exception ex) { uncertain = true; Status = "Outcome unconfirmed: " + ex.Message; }
    }

    private void AcceptResult(string json)
    {
        var response = JObject.Parse(json);
        var state = (string?)response["status"] ?? "";
        if (state == "pending") { Status = "Request sent to the host."; return; }
        if (state == "unknown") { uncertain = true; Status = (string?)response["message"] ?? "No confirmed response. Retry the same request."; return; }
        pending = null; uncertain = false; confirmation = null;
        var nested = (string?)response["result"]?["state"] ?? "";
        Status = state == "succeeded"
            ? nested == "PlacementPending" || nested == "ReconcileRequired" || nested == "CompletionPending" || nested == "InProgress"
                ? "Accepted; physical completion is pending. Check Deliveries or Contracts."
                : "Completed: " + pendingTitle
            : "Refused: " + ((string?)response["message"] ?? "The host did not accept this action.");
        refreshRequested = true;
    }
}