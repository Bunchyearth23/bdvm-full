using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;

namespace BDVM.Adapters;

public sealed class UnityMissionLifecyclePort : IMissionSettlementPort
{
    public WorldOwnershipOutcome InspectReservation(string missionId, IReadOnlyList<string> persistentCarGuids)
        => InspectState(missionId, persistentCarGuids, JobState.Available, JobState.InProgress);

    public WorldOwnershipOutcome InspectStart(string missionId, IReadOnlyList<string> persistentCarGuids)
        => InspectState(missionId, persistentCarGuids, JobState.InProgress);

    public WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids)
        => InspectState(missionId, persistentCarGuids, JobState.Completed);

    public WorldOwnershipOutcome InspectCancellation(string missionId, IReadOnlyList<string> persistentCarGuids)
        => InspectState(missionId, persistentCarGuids, JobState.Available, JobState.Abandoned, JobState.Expired);

    public MissionSettlementObservation InspectSettlement(string missionId, IReadOnlyList<string> persistentCarGuids)
    {
        var outcome = InspectState(missionId, persistentCarGuids, JobState.Completed);
        if (outcome != WorldOwnershipOutcome.Applied)
            return new MissionSettlementObservation { Outcome = outcome, Detail = outcome == WorldOwnershipOutcome.Unknown ? "unity-job-settlement-unknown" : "unity-job-not-completed" };
        try
        {
            var job = AllJobs().Single(value => value != null && string.Equals(value.ID, missionId, StringComparison.Ordinal));
            var wage = job.GetWageForTheJob();
            if (float.IsNaN(wage) || float.IsInfinity(wage) || wage < 0f || wage > long.MaxValue)
                return new MissionSettlementObservation { Outcome = WorldOwnershipOutcome.NotApplied, Detail = "unity-job-wage-invalid" };
            return new MissionSettlementObservation { Outcome = WorldOwnershipOutcome.Applied, Revenue = Convert.ToInt64(Math.Round(wage)), Detail = "unity-exact-job-wage" };
        }
        catch
        {
            return new MissionSettlementObservation { Outcome = WorldOwnershipOutcome.Unknown, Detail = "unity-job-wage-unavailable" };
        }
    }

    private static WorldOwnershipOutcome InspectState(string missionId, IReadOnlyList<string> persistentCarGuids, params JobState[] acceptedStates)
    {
        if (JobsManager.Instance == null || string.IsNullOrWhiteSpace(missionId)) return WorldOwnershipOutcome.Unknown;
        var matches = AllJobs().Where(x => x != null && string.Equals(x.ID, missionId, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length != 1) return matches.Length == 0 ? WorldOwnershipOutcome.NotApplied : WorldOwnershipOutcome.Unknown;
        var expected = Normalize(persistentCarGuids);
        if (expected == null) return WorldOwnershipOutcome.NotApplied;
        var observed = JobCarGuids(matches[0]);
        if (observed == null) return WorldOwnershipOutcome.Unknown;
        if (!expected.SequenceEqual(observed, StringComparer.OrdinalIgnoreCase)) return WorldOwnershipOutcome.NotApplied;
        return acceptedStates.Contains(matches[0].State) ? WorldOwnershipOutcome.Applied : WorldOwnershipOutcome.NotApplied;
    }

    private static string[]? Normalize(IEnumerable<string>? values)
    {
        var normalized = (values ?? Array.Empty<string>()).Select(value => Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string[]? JobCarGuids(Job job)
    {
        if (job.tasks == null || TrainCarRegistry.Instance == null) return null;
        var cars = EnumerateTasks(job.tasks)
            .SelectMany(CarsForTask)
            .Where(car => car != null)
            .Distinct()
            .ToArray();
        if (cars.Length == 0) return null;
        var guids = new List<string>();
        foreach (var car in cars)
        {
            if (!TrainCarRegistry.Instance.logicCarToTrainCar.TryGetValue(car, out var trainCar) || trainCar == null || !Guid.TryParse(trainCar.CarGUID, out var parsed)) return null;
            guids.Add(parsed.ToString("D"));
        }
        return guids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<Task> EnumerateTasks(IEnumerable<Task> tasks)
    {
        foreach (var task in tasks.Where(task => task != null))
        {
            yield return task;
            IEnumerable<Task>? children = task is SequentialTasks
                ? AccessTools.Field(typeof(SequentialTasks), "tasks")?.GetValue(task) as IEnumerable<Task>
                : task is ParallelTasks
                    ? AccessTools.Field(typeof(ParallelTasks), "tasks")?.GetValue(task) as IEnumerable<Task>
                    : null;
            if (children == null) continue;
            foreach (var child in EnumerateTasks(children)) yield return child;
        }
    }

    private static IEnumerable<Car> CarsForTask(Task task)
    {
        if (task is WarehouseTask warehouseTask) return warehouseTask.cars ?? Enumerable.Empty<Car>();
        if (task is TransportTask) return AccessTools.Field(typeof(TransportTask), "cars")?.GetValue(task) as IEnumerable<Car> ?? Enumerable.Empty<Car>();
        return Enumerable.Empty<Car>();
    }

    private static IEnumerable<Job> AllJobs()
    {
        var field = AccessTools.Field(typeof(JobsManager), "allJobs");
        return field?.GetValue(JobsManager.Instance) as IEnumerable<Job> ?? JobsManager.Instance.currentJobs ?? Enumerable.Empty<Job>();
    }
}
