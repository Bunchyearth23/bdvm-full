using System.Collections.Generic;
using DV.ThingTypes;
using HarmonyLib;

namespace BDVM.Adapters;

/// <summary>
/// Removes vanilla license gates without deleting or rewriting their save data.
/// Other Career Manager systems (fees, statistics and owned vehicles) remain active.
/// </summary>
public static class UnityLicenseNeutralization
{
    public static void Install(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(JobLicenseAcquiredPatch)).Patch();
        harmony.CreateClassProcessor(typeof(GeneralLicenseAcquiredPatch)).Patch();
        harmony.CreateClassProcessor(typeof(CarLicenseGatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(JobLicenseGatePatch)).Patch();
        harmony.CreateClassProcessor(typeof(JobLicenseObtainablePatch)).Patch();
        harmony.CreateClassProcessor(typeof(GeneralLicenseObtainablePatch)).Patch();
        harmony.CreateClassProcessor(typeof(MissingJobLicensesPatch)).Patch();
        harmony.CreateClassProcessor(typeof(ConcurrentJobLimitPatch)).Patch();
        harmony.CreateClassProcessor(typeof(TrainLengthLimitPatch)).Patch();
    }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsJobLicenseAcquired))]
    private static class JobLicenseAcquiredPatch { private static void Postfix(ref bool __result) => __result = true; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsGeneralLicenseAcquired))]
    private static class GeneralLicenseAcquiredPatch { private static void Postfix(ref bool __result) => __result = true; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsLicensedForCar))]
    private static class CarLicenseGatePatch { private static void Postfix(ref bool __result) => __result = true; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsLicensedForJob))]
    private static class JobLicenseGatePatch { private static void Postfix(ref bool __result) => __result = true; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsJobLicenseObtainable))]
    private static class JobLicenseObtainablePatch { private static void Postfix(ref bool __result) => __result = false; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.IsGeneralLicenseObtainable))]
    private static class GeneralLicenseObtainablePatch { private static void Postfix(ref bool __result) => __result = false; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.GetMissingLicensesForJob))]
    private static class MissingJobLicensesPatch
    {
        private static void Postfix(ref HashSet<JobLicenseType_v2> __result) => __result = new HashSet<JobLicenseType_v2>();
    }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.GetNumberOfAllowedConcurrentJobs))]
    private static class ConcurrentJobLimitPatch { private static void Postfix(ref int __result) => __result = int.MaxValue; }

    [HarmonyPatch(typeof(LicenseManager), nameof(LicenseManager.GetMaxNumberOfCarsPerJobWithAcquiredJobLicenses))]
    private static class TrainLengthLimitPatch { private static void Postfix(ref int __result) => __result = int.MaxValue; }

}
