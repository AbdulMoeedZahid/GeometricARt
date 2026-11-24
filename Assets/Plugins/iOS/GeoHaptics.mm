// GeometricARt haptics (iOS): a light selection tick (UISelectionFeedbackGenerator) for snaps and hold menus.
// Called from Assets/GeoART/App/Haptics.cs through [DllImport("__Internal")]. Handheld.Vibrate() is the full system buzz,
// so it can't be used for a tick. The generator is kept and re-prepared after each tick so the next one has no latency.
// UIKit must be used on the main thread; Unity's scripting thread is the main thread on iOS, but a caller on another
// thread is dispatched to the main queue.

#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *s_geoHapticsGenerator = nil;

static void GeoHapticsSelectionOnMain(void)
{
    if (s_geoHapticsGenerator == nil)
    {
        s_geoHapticsGenerator = [[UISelectionFeedbackGenerator alloc] init];
        [s_geoHapticsGenerator prepare];
    }
    [s_geoHapticsGenerator selectionChanged];
    [s_geoHapticsGenerator prepare];
}

extern "C" void GeoHapticsSelection(void)
{
    if ([NSThread isMainThread])
        GeoHapticsSelectionOnMain();
    else
        dispatch_async(dispatch_get_main_queue(), ^{ GeoHapticsSelectionOnMain(); });
}
