using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace TacticalRequests;

internal sealed class QuickSlAdapter
{
    internal const string Signature = "QuickSL/0.3.6-beta/tactical-1";
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Type coordinator;
    private QuickSlAdapter(Type coordinator) => this.coordinator = coordinator;
    internal static QuickSlAdapter? Detect()
    {
        if (!ModManager.Mods.Any(mod => mod.state == ModLoadState.Loaded && mod.manifest?.id == "QuickSL" && mod.manifest.version == "0.3.6-beta")) return null;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("QuickSlMod.QuickSlVoteCoordinator")).FirstOrDefault(type => type != null);
        if (type == null || type.GetMethod("RequestVote", Flags, Type.EmptyTypes) == null
            || type.GetMethod("CancelHostVote", Flags) == null || type.GetMethod("ShowVotePopupAsync", Flags) == null
            || type.GetMethod("SubmitLocalVote", Flags, new[] { typeof(bool) }) == null
            || new[] { "_nextVoteId", "_localVoteId", "_hostVote", "_localVoteResponded", "_requestPending", "_preparingVoteId" }.Any(name => type.GetField(name, Flags) == null)) return null;
        return new QuickSlAdapter(type);
    }
    private object? Field(string name) => coordinator.GetField(name, Flags)!.GetValue(null);
    internal ulong NextVote => (ulong)Field("_nextVoteId")!;
    internal ulong LocalVote => (ulong)Field("_localVoteId")!;
    internal bool Busy => Field("_hostVote") != null || LocalVote != 0 || (bool)Field("_requestPending")! || (ulong)Field("_preparingVoteId")! != 0;
    internal void Start() => coordinator.GetMethod("RequestVote", Flags)!.Invoke(null, null);
    internal void Accept() => coordinator.GetMethod("SubmitLocalVote", Flags)!.Invoke(null, new object[] { true });
    internal void Cancel(ulong vote)
    {
        var state = Field("_hostVote");
        if (state == null || (ulong)state.GetType().GetProperty("Id")!.GetValue(state)! != vote
            || (bool)state.GetType().GetProperty("PreparationStarted")!.GetValue(state)!) return;
        var method = coordinator.GetMethod("CancelHostVote", Flags)!;
        method.Invoke(null, new[] { Enum.Parse(method.GetParameters()[0].ParameterType, "Rejected") });
    }
    internal void InstallPopupHook()
    {
        var method = coordinator.GetMethod("ShowVotePopupAsync", Flags) ?? throw new MissingMethodException("QuickSL vote popup");
        new Harmony("Wanderer.TacticalRequests.QuickSl").Patch(method,
            prefix: new HarmonyMethod(typeof(QuickSlAdapter), nameof(BeforePopup)));
    }
    private static bool BeforePopup(ulong voteId, ref Task __result)
    {
        if (QuickSlBridge.Active?.AcceptBoundVote(voteId) != true) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
