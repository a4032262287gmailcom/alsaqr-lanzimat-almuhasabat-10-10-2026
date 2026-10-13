using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;

namespace AlSaqr.Accounting;

internal static class ReportPrintService
{
    public static void Preview(IWin32Window owner, DataTable table, string title, Color brandColor)
    {
        if (table.Columns.Count == 0)
            throw new InvalidOperationException("لا يحتوي التقرير الحالي على أعمدة قابلة للطباعة.");

        using var document = new PrintDocument();
        document.DocumentName = "الصقر - " + title;
        document.DefaultPageSettings.Landscape = table.Columns.Count > 5;
        document.DefaultPageSettings.Margins = new Margins(36, 36, 44, 42);

        var a4 = document.PrinterSettings.PaperSizes
            .Cast<PaperSize>()
            .FirstOrDefault(size => size.Kind == PaperKind.A4);
        if (a4 is not null)
            document.DefaultPageSettings.PaperSize = a4;

        var rowIndex = 0;
        var columnStart = 0;
        document.BeginPrint += (_, _) =>
        {
            rowIndex = 0;
            columnStart = 0;
        };

        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("تعذر الحصول على سطح الرسم الخاص بالطباعة.");
            var bounds = e.MarginBounds;
            var y = (float)bounds.Top;
            var pageColumnLimit = document.DefaultPageSettings.Landscape ? 8 : 5;
            var columnEnd = Math.Min(columnStart + pageColumnLimit, table.Columns.Count);
            var visibleColumnCount = columnEnd - columnStart;

            using var titleFont = new Font("Segoe UI", 14F, FontStyle.Bold);
            using var metaFont = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            using var headerFont = new Font("Segoe UI", visibleColumnCount >= 7 ? 7F : 8.5F, FontStyle.Bold);
            using var cellFont = new Font("Segoe UI", visibleColumnCount >= 7 ? 6.8F : 8F, FontStyle.Regular);
            using var titleBrush = new SolidBrush(brandColor);
            using var subTitleBrush = new SolidBrush(Color.FromArgb(78, 89, 104));
            using var headerBrush = new SolidBrush(brandColor);
            using var rowBrush = new SolidBrush(Color.White);
            using var alternateBrush = new SolidBrush(Color.FromArgb(246, 248, 251));
            using var borderPen = new Pen(Color.FromArgb(205, 213, 224), 0.6F);
            using var rtl = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.DirectionRightToLeft,
                Trimming = StringTrimming.EllipsisCharacter
            };

            graphics.DrawString("الصقر | نظام المحاسبة", titleFont, titleBrush,
                new RectangleF(bounds.Left, y, bounds.Width, 27), rtl);
            y += 29;
            graphics.DrawString(title, titleFont, titleBrush,
                new RectangleF(bounds.Left, y, bounds.Width, 27), rtl);
            y += 29;
            var meta = $"تاريخ الطباعة: {DateTime.Now:yyyy-MM-dd HH:mm}    |    عدد السجلات: {table.Rows.Count}";
            graphics.DrawString(meta, metaFont, subTitleBrush,
                new RectangleF(bounds.Left, y, bounds.Width, 20), rtl);
            y += 24;

            var columnWidth = bounds.Width / (float)Math.Max(1, visibleColumnCount);
            const float headerHeight = 28F;
            const float rowHeight = 23F;

            for (var visibleIndex = 0; visibleIndex < visibleColumnCount; visibleIndex++)
            {
                var columnIndex = columnStart + visibleIndex;
                var cellRect = new RectangleF(bounds.Left + visibleIndex * columnWidth, y, columnWidth, headerHeight);
                graphics.FillRectangle(headerBrush, cellRect);
                graphics.DrawRectangle(borderPen, Rectangle.Round(cellRect));
                graphics.DrawString(table.Columns[columnIndex].ColumnName, headerFont, Brushes.White,
                    RectangleF.Inflate(cellRect, -3, -2), rtl);
            }
            y += headerHeight;

            while (rowIndex < table.Rows.Count && y + rowHeight <= bounds.Bottom)
            {
                var brush = rowIndex % 2 == 0 ? rowBrush : alternateBrush;
                var row = table.Rows[rowIndex];

                for (var visibleIndex = 0; visibleIndex < visibleColumnCount; visibleIndex++)
                {
                    var columnIndex = columnStart + visibleIndex;
                    var cellRect = new RectangleF(bounds.Left + visibleIndex * columnWidth, y, columnWidth, rowHeight);
                    graphics.FillRectangle(brush, cellRect);
                    graphics.DrawRectangle(borderPen, Rectangle.Round(cellRect));

                    var value = row.IsNull(columnIndex)
                        ? string.Empty
                        : Convert.ToString(row[columnIndex], CultureInfo.GetCultureInfo("ar-SA")) ?? string.Empty;
                    graphics.DrawString(value, cellFont, Brushes.Black,
                        RectangleF.Inflate(cellRect, -3, -2), rtl);
                }

                y += rowHeight;
                rowIndex++;
            }

            if (rowIndex < table.Rows.Count)
            {
                e.HasMorePages = true;
            }
            else if (columnEnd < table.Columns.Count)
            {
                columnStart = columnEnd;
                rowIndex = 0;
                e.HasMorePages = true;
            }
            else
            {
                e.HasMorePages = false;
            }
        };

        using var preview = new PrintPreviewDialog
        {
            Document = document,
            WindowState = FormWindowState.Maximized,
            Text = "معاينة الطباعة — " + title
        };
        preview.ShowDialog(owner);
    }
}