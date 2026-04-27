using OLEDSaver;
using System.Drawing;

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}

static void AssertSame<T>(T expected, T actual, string name)
    where T : class
{
    if (!ReferenceEquals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected same reference");
    }
}

static void AssertNotEqual<T>(T unexpected, T actual, string name)
{
    if (EqualityComparer<T>.Default.Equals(unexpected, actual))
    {
        throw new InvalidOperationException($"{name}: did not expect {actual}");
    }
}

AssertEqual(
    expected: true,
    actual: InactivityDecisions.ShouldTurnOffDisplay(
        screenOffEnabled: true,
        screenOff: false,
        idleSeconds: 60,
        timeoutSeconds: 60,
        isVideoPlaying: false),
    name: "turns off display at timeout when enabled and no video");

AssertEqual(
    expected: false,
    actual: InactivityDecisions.ShouldTurnOffDisplay(
        screenOffEnabled: true,
        screenOff: false,
        idleSeconds: 59.9,
        timeoutSeconds: 60,
        isVideoPlaying: false),
    name: "does not turn off display before timeout");

AssertEqual(
    expected: false,
    actual: InactivityDecisions.ShouldTurnOffDisplay(
        screenOffEnabled: true,
        screenOff: false,
        idleSeconds: 60,
        timeoutSeconds: 60,
        isVideoPlaying: true),
    name: "does not turn off display while video is playing");

AssertEqual(
    expected: false,
    actual: InactivityDecisions.ShouldTurnOffDisplay(
        screenOffEnabled: false,
        screenOff: false,
        idleSeconds: 60,
        timeoutSeconds: 60,
        isVideoPlaying: false),
    name: "does not turn off display when feature disabled");

AssertEqual(
    expected: false,
    actual: InactivityDecisions.ShouldTurnOffDisplay(
        screenOffEnabled: true,
        screenOff: true,
        idleSeconds: 60,
        timeoutSeconds: 60,
        isVideoPlaying: false),
    name: "does not turn off display when already off");

int snapshotLoads = 0;
var snapshotCache = new CachedSnapshot<int>(() =>
{
    snapshotLoads++;
    return new[] { snapshotLoads };
});

int[] firstSnapshot = snapshotCache.Get();
int[] secondSnapshot = snapshotCache.Get();
AssertSame(firstSnapshot, secondSnapshot, "cached snapshot is reused before invalidation");
AssertEqual(1, snapshotLoads, "snapshot provider runs once before invalidation");
snapshotCache.Invalidate();
int[] thirdSnapshot = snapshotCache.Get();
AssertEqual(2, snapshotLoads, "snapshot provider runs again after invalidation");
AssertEqual(2, thirdSnapshot[0], "snapshot contains refreshed value after invalidation");

DateTime now = new DateTime(2026, 4, 27, 10, 0, 0, DateTimeKind.Utc);
int valueLoads = 0;
var expiringCache = new ExpiringValueCache<int, string>(
    lifetime: TimeSpan.FromSeconds(2),
    nowProvider: () => now);

string firstValue = expiringCache.GetOrAdd(7, () =>
{
    valueLoads++;
    return "value-" + valueLoads;
});
string secondValue = expiringCache.GetOrAdd(7, () =>
{
    valueLoads++;
    return "value-" + valueLoads;
});
AssertEqual("value-1", firstValue, "expiring cache stores initial value");
AssertEqual("value-1", secondValue, "expiring cache reuses value before expiry");
AssertEqual(1, valueLoads, "expiring cache factory runs once before expiry");

now = now.AddSeconds(2);
string thirdValue = expiringCache.GetOrAdd(7, () =>
{
    valueLoads++;
    return "value-" + valueLoads;
});
AssertEqual("value-2", thirdValue, "expiring cache refreshes at expiry");
AssertEqual(2, valueLoads, "expiring cache factory runs after expiry");

var offscreenRegionA = OverlayRegionState.Create(
    relativeRect: new Rectangle(200, 20, 50, 50),
    overlaySize: new Size(100, 100),
    roundedCorners: true);
var offscreenRegionB = OverlayRegionState.Create(
    relativeRect: new Rectangle(300, 30, 50, 50),
    overlaySize: new Size(100, 100),
    roundedCorners: true);
AssertEqual(offscreenRegionA, offscreenRegionB, "offscreen cutouts normalize to the same region state");

var onscreenRegionA = OverlayRegionState.Create(
    relativeRect: new Rectangle(20, 20, 50, 50),
    overlaySize: new Size(100, 100),
    roundedCorners: true);
var onscreenRegionB = OverlayRegionState.Create(
    relativeRect: new Rectangle(25, 20, 50, 50),
    overlaySize: new Size(100, 100),
    roundedCorners: true);
AssertNotEqual(onscreenRegionA, onscreenRegionB, "onscreen cutout movement changes region state");

var handleCache = new CachedWindowHandles();
AssertEqual(false, handleCache.TryGetValid(_ => true, out var emptyHandles), "empty handle cache is not reusable");
AssertEqual(0, emptyHandles.Count, "empty handle cache returns empty snapshot");

var handleOne = new IntPtr(100);
var handleTwo = new IntPtr(200);
handleCache.Replace(new[] { handleOne, handleTwo });
AssertEqual(true, handleCache.TryGetValid(handle => handle != IntPtr.Zero, out var validHandles), "valid handle cache is reusable");
AssertEqual(2, validHandles.Count, "valid handle cache returns both handles");
AssertEqual(handleOne, validHandles[0], "valid handle cache preserves first handle");
AssertEqual(handleTwo, validHandles[1], "valid handle cache preserves second handle");

AssertEqual(false, handleCache.TryGetValid(handle => handle != handleTwo, out var invalidHandles), "invalid handle cache is not reusable");
AssertEqual(0, invalidHandles.Count, "invalid handle cache returns empty snapshot");
AssertEqual(false, handleCache.TryGetValid(_ => true, out _), "invalid handle cache is cleared");
