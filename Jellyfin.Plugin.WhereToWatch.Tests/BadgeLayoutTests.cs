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

    [Fact]
    public void Visible_region_trims_the_sides_of_an_image_wider_than_the_card()
    {
        // The client shows the image centred and cropped to the card, so a square image in a 2:3 card loses 50px each side.
        Assert.Equal(new BadgeRect(50, 0, 200, 300), BadgeLayout.VisibleRegion(300, 300, 2.0 / 3));
    }

    [Fact]
    public void Visible_region_trims_top_and_bottom_of_an_image_taller_than_the_card()
    {
        Assert.Equal(new BadgeRect(0, 50, 200, 300), BadgeLayout.VisibleRegion(200, 400, 2.0 / 3));
    }

    [Fact]
    public void Visible_region_is_the_whole_image_when_the_shapes_match()
    {
        Assert.Equal(new BadgeRect(0, 0, 200, 300), BadgeLayout.VisibleRegion(200, 300, 2.0 / 3));
    }

    [Fact]
    public void Badges_are_sized_and_inset_relative_to_the_visible_region()
    {
        var region = BadgeLayout.VisibleRegion(300, 300, 2.0 / 3);

        var left = BadgeLayout.Compute(300, 300, 1, Settings(BadgeCorner.BottomLeft), region);
        var right = BadgeLayout.Compute(300, 300, 1, Settings(BadgeCorner.BottomRight), region);

        // Region is 200 wide: width 22% = 44, height 22, margin 3% = 6 on every side of the region, not of the image.
        Assert.Equal([new BadgeRect(56, 272, 44, 22)], left);
        Assert.Equal([new BadgeRect(200, 272, 44, 22)], right);
    }
}
