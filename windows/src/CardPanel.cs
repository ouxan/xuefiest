using System;
using System.Windows;
using System.Windows.Controls;

namespace Xuefiest
{
    // Responsive grid for game cards. Picks the number of columns that fits and scales the cards
    // uniformly so each row spans the full width. Cards are designed at their natural size inside
    // a Viewbox; the panel only decides their final size.
    public class CardPanel : Panel
    {
        public double HorizontalGap = 18;
        public double VerticalGap = 22;
        // Maximum upscale; beyond this a new column is added.
        public double MaxScale = 1.35;

        private int _cols = 1;
        private double _itemWidth, _itemHeight;

        protected override Size MeasureOverride(Size available)
        {
            int count = InternalChildren.Count;
            if (count == 0)
                return new Size(0, 0);

            // Natural (unscaled) card size.
            Size natural = new Size(0, 0);
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (child.DesiredSize.Width > natural.Width)
                    natural = child.DesiredSize;
            }
            if (natural.Width <= 0 || natural.Height <= 0)
                return new Size(0, 0);

            double width = double.IsInfinity(available.Width)
                               ? count * natural.Width + (count - 1) * HorizontalGap
                               : available.Width;

            _cols = Math.Max(1, (int)Math.Floor((width + HorizontalGap) / (natural.Width + HorizontalGap)));
            _itemWidth = (width - HorizontalGap * (_cols - 1)) / _cols;
            _itemWidth = Math.Max(natural.Width * 0.75, Math.Min(_itemWidth, natural.Width * MaxScale));
            _itemHeight = natural.Height * _itemWidth / natural.Width;

            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(_itemWidth, _itemHeight));

            int rows = (count + _cols - 1) / _cols;
            double height = rows * _itemHeight + (rows - 1) * VerticalGap;
            return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                int row = i / _cols;
                int col = i % _cols;
                InternalChildren[i].Arrange(new Rect(col * (_itemWidth + HorizontalGap),
                                                     row * (_itemHeight + VerticalGap),
                                                     _itemWidth, _itemHeight));
            }
            return finalSize;
        }
    }
}
