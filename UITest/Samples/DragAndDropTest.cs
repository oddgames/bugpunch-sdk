using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ODDGames.UITest.Samples
{
    /// <summary>
    /// Demonstrates drag and swipe interactions.
    /// Shows various drag patterns for scrolling, swiping, and drag-and-drop.
    /// </summary>
    [UITest(
        Scenario = 9004,
        Feature = "Drag",
        Story = "User can drag and swipe UI elements",
        Severity = TestSeverity.Normal,
        Description = "Tests drag gestures, scrolling, and swipe interactions",
        Tags = new[] { "sample", "drag", "swipe", "scroll" }
    )]
    public class DragAndDropTest : UITestBehaviour
    {
        protected override async UniTask Test()
        {
            // Step 1: Wait for UI
            using (BeginStep("Wait for Sample UI"))
            {
                await Wait("SampleDragPanel", seconds: 10);
                LogStep("Drag panel loaded");
            }

            // Step 2: Scroll a list vertically
            using (BeginStep("Scroll List Down"))
            {
                var scrollView = await Find<RectTransform>("ScrollView", throwIfMissing: false);
                if (scrollView != null)
                {
                    // Drag upward to scroll down (content moves up)
                    await Drag("ScrollView", new Vector2(0, -300), duration: 0.5f);
                    await Wait(1);
                    LogStep("Scrolled list down");
                    CaptureScreenshot("after_scroll_down");

                    // Scroll back up
                    await Drag("ScrollView", new Vector2(0, 300), duration: 0.5f);
                    LogStep("Scrolled list up");
                }
                else
                {
                    LogStep("No ScrollView found - skipping scroll test");
                }
            }

            // Step 3: Horizontal swipe (e.g., carousel)
            using (BeginStep("Swipe Carousel"))
            {
                var carousel = await Find<RectTransform>(
                    new[] { "*Carousel*", "*HorizontalScroll*", "*Gallery*" },
                    throwIfMissing: false,
                    seconds: 3
                );

                if (carousel != null)
                {
                    // Swipe left
                    await Drag(carousel.name, new Vector2(-200, 0), duration: 0.3f);
                    await Wait(1);
                    CaptureScreenshot("carousel_swiped_left");

                    // Swipe right
                    await Drag(carousel.name, new Vector2(200, 0), duration: 0.3f);
                    LogStep("Carousel swiped both directions");
                }
                else
                {
                    LogStep("No carousel found - skipping swipe test");
                }
            }

            // Step 4: Drag from screen center
            using (BeginStep("Drag from Center"))
            {
                // Drag diagonally from screen center
                await Drag(new Vector2(150, 150), duration: 0.5f);
                LogStep("Dragged from screen center");
            }

            // Step 5: Drag between specific positions
            using (BeginStep("Drag Between Positions"))
            {
                // Simulate dragging from one side to another
                Vector2 start = new Vector2(Screen.width * 0.2f, Screen.height * 0.5f);
                Vector2 end = new Vector2(Screen.width * 0.8f, Screen.height * 0.5f);

                await DragFromTo(start, end, duration: 0.8f);
                LogStep("Dragged across screen");
                CaptureScreenshot("after_drag");
            }

            // Step 6: Drag and drop item
            using (BeginStep("Drag and Drop Item"))
            {
                var draggable = await Find<RectTransform>("DraggableItem", throwIfMissing: false);
                var dropZone = await Find<RectTransform>("DropZone", throwIfMissing: false);

                if (draggable != null && dropZone != null)
                {
                    // Calculate drag vector from item to drop zone
                    Vector3[] draggableCorners = new Vector3[4];
                    Vector3[] dropZoneCorners = new Vector3[4];
                    draggable.GetWorldCorners(draggableCorners);
                    dropZone.GetWorldCorners(dropZoneCorners);

                    Vector2 itemCenter = (draggableCorners[0] + draggableCorners[2]) / 2f;
                    Vector2 targetCenter = (dropZoneCorners[0] + dropZoneCorners[2]) / 2f;
                    Vector2 dragDirection = targetCenter - itemCenter;

                    await Drag("DraggableItem", dragDirection, duration: 0.6f);
                    LogStep("Item dragged to drop zone");
                    CaptureScreenshot("after_drop");
                }
                else
                {
                    LogStep("Drag and drop elements not found - skipping");
                }
            }

            LogStep("Drag and drop test completed");
        }
    }
}
