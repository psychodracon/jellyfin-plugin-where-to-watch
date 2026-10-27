using Jellyfin.Plugin.WhereToWatch.Rendering;

namespace Jellyfin.Plugin.WhereToWatch.Tests;

public class BadgeLayoutTests
{
    private static LayoutSettings Settings(BadgeCorner corner) => new(
        Corner: corner,
        WidthPercent: 22,
        AspectRatio: 2,
        MarginPercent: 3,
        SpacingPercent: 1);

    [Fact]
    public void Bottom_left_stacks_upward_with_the_first_badge_at_the_bottom()
    {
        var rects = BadgeLayout.Compute(1000, 1500, 3, Settings(BadgeCorner.BottomLeft));

        Assert.Equal(
            [
                new BadgeRect(30, 1360, 220, 110),
                new BadgeRect(30, 1240, 220, 110),
                new BadgeRect(30, 1120, 220, 110),
            ],
            rects);
    }

    [Fact]
    public void Bottom_right_anchors_to_the_right_edge()
    {
        var rects = BadgeLayout.Compute(1000, 1500, 1, Settings(BadgeCorner.BottomRight));

        Assert.Equal([new BadgeRect(750, 1360, 220, 110)], rects);
    }

    [Fact]
    public void Top_corners_stack_downward_with_the_first_badge_at_the_top()
    {
        var left = BadgeLayout.Compute(1000, 1500, 3, Settings(BadgeCorner.TopLeft));
        var right = BadgeLayout.Compute(1000, 1500, 2, Settings(BadgeCorner.TopRight));

        Assert.Equal(
            [
                new BadgeRect(30, 30, 220, 110),
                new BadgeRect(30, 150, 220, 110),
                new BadgeRect(30, 270, 220, 110),
            ],
            left);
        Assert.Equal([new BadgeRect(750, 30, 220, 110), new BadgeRect(750, 150, 220, 110)], right);
    }

    [Fact]
    public void Drops_badges_that_do_not_fit_inside_the_poster()
    {
        // Usable height is 300 - 2 * 30 = 240: two badges (110 + 10 + 110) fit, a third would not.
        var rects = BadgeLayout.Compute(1000, 300, 3, Settings(BadgeCorner.BottomLeft));

        Assert.Equal(2, rects.Count);
    }

    [Fact]
    public void Places_nothing_when_there_are_no_badges()
    {
        Assert.Empty(BadgeLayout.Compute(1000, 1500, 0, Settings(BadgeCorner.BottomLeft)));
    }
}
