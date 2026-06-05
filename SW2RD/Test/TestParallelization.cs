using Xunit;

// Every SW-attached test class drives the SAME single SOLIDWORKS instance
// through the static SWTestFixture.SwApp. SOLIDWORKS' automation API is
// apartment-threaded and is NOT safe to call concurrently from multiple
// threads against one running instance.
//
// xunit's AssemblyRunner runs distinct test classes as separate collections in
// PARALLEL by default, so TestCommon / TestJointAxisFlipped / TestExportHelper /
// TestSWAttached were all hammering the one shared SW app at once. That race
// corrupts SW's COM state and surfaces nondeterministically as either:
//   - the SW process dying  -> "RPC server is unavailable" (0x800706BA) on every
//     subsequent call, or
//   - the COM proxy disconnecting -> "disconnected from its clients"
//     (RPC_E_DISCONNECTED) and the out-of-process test thread blocking forever
//     (the "is SW hung?" symptom).
// Running a single SW-attached class in isolation never reproduced it because
// there was no cross-class concurrency.
//
// Disable test parallelization assembly-wide so the shared SW instance is only
// ever touched by one thread at a time. This is the correct constraint for a
// suite built around one external, single-threaded COM server; do NOT re-enable
// parallelization without giving each parallel collection its own SW instance.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
