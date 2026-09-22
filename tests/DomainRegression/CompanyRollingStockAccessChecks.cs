using System;
using BDVM.Domain;
internal static class CompanyRollingStockAccessChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var state = new VehicleAcquisitionSnapshot();
        var guid = Guid.NewGuid().ToString("D");
        var asset = FleetAsset.Create("locomotive", guid); state.Assets.Assets.Add(asset);
        var owner = new AssetOwnership { AssetId = asset.AssetId, Owner = AssetOwnerRef.Company("a") }; state.Ownership.Add(owner);
        var company = new CompanyState { CompanyId = "a", LeaderId = "member" }; company.Members.Add("member"); state.Economy.Companies.Add(company);
        var member = new PlayerEconomicState { PlayerId = "member", CompanyId = "a" }; state.Economy.Players.Add(member);
        state.Economy.Players.Add(new PlayerEconomicState { PlayerId = "outsider", CompanyId = "b" });
        state.Economy.Players.Add(new PlayerEconomicState { PlayerId = "host" });
        var policy = new CompanyRollingStockAccess(state);
        Check(policy.Allows("member", guid), "Company member denied");
        Check(policy.Allows("member", Guid.Parse(guid).ToString("N")), "GUID format changed authorization");
        Check(!policy.Allows("outsider", guid) && !policy.Allows("host", guid), "Nonmember or host bypassed company ownership");
        Check(!policy.Allows("", guid), "Anonymous actor accepted");
        company.Members.Remove("member");
        Check(!policy.Allows("member", guid), "Membership revocation kept an existing control authorized");
        company.Members.Add("member"); member.CompanyId = "b";
        Check(!policy.Allows("member", guid), "Stale member list authorized a player of another company");
        member.CompanyId = "a"; company.Liquidating = true;
        Check(!policy.Allows("member", guid), "Liquidating company remains operable");
        company.Liquidating = false; owner.Owner = AssetOwnerRef.Company("b");
        Check(!policy.Allows("member", guid), "Ownership transfer did not revoke former company");
        owner.Owner = AssetOwnerRef.Player("owner");
        Check(policy.Allows("outsider", guid), "Unrequested personal ownership policy changed");
        Check(policy.Allows("outsider", Guid.NewGuid().ToString()), "Unmanaged stock was blocked");
        Check(!policy.Allows("member", "invalid"), "Malformed physical identity allowed");
        state.Assets.Assets.Add(FleetAsset.Create("duplicate", guid));
        Check(!new CompanyRollingStockAccess(state).Allows("member", guid), "Ambiguous car binding accepted");
        state.Assets.Assets.RemoveAt(1); state.Ownership.Clear();
        Check(!new CompanyRollingStockAccess(state).Allows("member", guid), "Managed asset without owner accepted");
    }
}
