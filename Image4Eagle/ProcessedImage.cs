using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Image4Eagle
{
    using Color = System.Drawing.Color;
    using Brushes = System.Drawing.Brushes;

    public class ProcessedImage
    {
        private class Node
        {
            public List<Node> Next = new List<Node>();
            public Node Prev;
            public int MinX;
            public int MaxX;
            public bool Visited;
        }

        private int _width;
        private int _height;        
        private bool[,] _pixels;
        private Node[,] _nodesPositive;
        private Node[,] _nodesNegative;
        private List<Tuple<List<PointF>, List<PointF>>> _segments;
        private SizeF _segmentsSize;

        private bool[] _flip;
        private float _scale;
        private int _line;


        public ProcessedImage(Bitmap bitmap)
        {
            _width = bitmap.Width;
            _height = bitmap.Height;
            _pixels = new bool[bitmap.Height, bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    _pixels[y, x] = (bitmap.GetPixel(x, y).GetBrightness() >= 0.5f);

            GenerateNodes(ref _nodesPositive, true);
            GenerateNodes(ref _nodesNegative, false);
        }

        public void GenerateEagleScript(TextWriter target, int layer)
        {
            var cc = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                target.WriteLine("GRID MM 0.001");
                target.WriteLine("LAYER " + layer);
                foreach (var s in _segments)
                {
                    if (s.Item1.Count < 3)
                        continue;

                    target.Write("POLY {0:0.000}", _line / 1000.0f);
                    for (int i = 0; i < s.Item1.Count; i++)
                        target.Write(" ({0:0.000} {1:0.000})", s.Item1[i].X + s.Item2[i].X, s.Item1[i].Y + s.Item2[i].Y);
                    target.WriteLine(";");
                    // Only for board, cannot be used in library
                    //tw.WriteLine("CHANGE THERMALS OFF ({0:0.000} {1:0.000});", s[0].X / 10.0f, s[0].Y / 10.0f);
                    //tw.WriteLine("CHANGE ISOLATE 0.000 ({0:0.000} {1:0.000});", s[0].X / 10.0f, s[0].Y / 10.0f);
                }
                target.WriteLine("GRID LAST");
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = cc;
            }
        }

        public BitmapSource GeneratePreview()
        {
            const int h = 1980;
            using (Bitmap bmp = new Bitmap(1 + (int)((_segmentsSize.Width / _segmentsSize.Height) * h), h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.ScaleTransform(bmp.Width / _segmentsSize.Width, bmp.Height / _segmentsSize.Height);
                    g.Clear(Color.White);
                    foreach (var seg in _segments)
                        g.FillPolygon(Brushes.Black, seg.Item1.ToArray());
                }

                byte[] px = new byte[bmp.Width * bmp.Height];
                for (int y = bmp.Height - 1, i = 0; y >= 0; y--)
                    for (int x = 0; x < bmp.Width; x++, i++)
                        px[i] = (byte)Math.Max(0, Math.Min(255, (int)(255.0f * bmp.GetPixel(x, y).GetBrightness())));

                WriteableBitmap wb = new WriteableBitmap(bmp.Width, bmp.Height, 96, 96, PixelFormats.Gray8, null);
                wb.Lock();
                wb.WritePixels(new System.Windows.Int32Rect(0, 0, bmp.Width, bmp.Height), px, bmp.Width, 0);
                wb.Unlock();
                return wb;
            }                
        }
        
        public void Generate(bool flipx, bool flipy, int line_um, int dim_um, bool whiteIsBackground = true)
        {
            _flip = new bool[] { flipx, flipy };
            _line = line_um;
            _segments = new List<Tuple<List<PointF>, List<PointF>>>();
            _scale = dim_um / (float)Math.Max(_width, _height);

            var nodes = whiteIsBackground ? _nodesPositive : _nodesNegative;

            for (int y = 0, ye = _height + 2; y < ye; y++)
                for (int x = 0, xe = _width + 2; x < xe; x++)
                    if (nodes[y, x] != null)
                        nodes[y, x].Visited = false;

            for (int y = 0, ye = _height + 2; y < ye; y++)
            {
                for (int x = 0, xe = _width + 2; x < xe; x++)
                {
                    if (nodes[y, x] != null && !nodes[y, x].Visited)
                    {
                        List<PointF> s = new List<PointF>();
                        List<PointF> o = new List<PointF>();
                        TraverseNode(nodes[y, x], _height + 1 - y, s, o);
                        if (s.Count > 2)
                        {
                            if (s[s.Count - 1] != s[0])
                            {
                                s.Add(s[0]);
                                o.Add(new PointF(0, 0));
                            }
                            _segments.Add(new Tuple<List<PointF>, List<PointF>>(s, o));
                        }
                    }
                }
            }

            if (_segments.Count > 0)
            {
                float minX = _segments.Select(x => x.Item1.Select(y => y.X).Min()).Min();
                float maxX = _segments.Select(x => x.Item1.Select(y => y.X).Max()).Max();
                float minY = _segments.Select(x => x.Item1.Select(y => y.Y).Min()).Min();
                float maxY = _segments.Select(x => x.Item1.Select(y => y.Y).Max()).Max();
                _segmentsSize = new SizeF(maxX - minX, maxY - minY);
                foreach (var s in _segments)
                    for (int i = 0; i < s.Item1.Count; i++)
                        s.Item1[i] = new PointF(s.Item1[i].X - minX, s.Item1[i].Y - minY);
            }
            else
                _segmentsSize = new SizeF(1, 1);
        }

        private void GenerateNodes(ref Node[,] nodes, bool whiteIsBackground = true)
        {
            nodes = new Node[_height + 2, _width + 2];

            // Create array of visited pixels (all background pixels are visited already)
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    // Skip background
                    if (_pixels[y, x] == whiteIsBackground)
                        continue;

                    // Pixel on the left -> same node (new node otherwise)
                    if (nodes[y + 1, x] != null)
                        nodes[y + 1, x + 1] = nodes[y + 1, x];
                    else
                    {
                        nodes[y + 1, x + 1] = new Node();
                        nodes[y + 1, x + 1].MinX = x;
                    }
                    nodes[y + 1, x + 1].MaxX = x;

                    // No parent and pixel above -> my parent
                    if (nodes[y + 1, x + 1].Prev == null && nodes[y, x + 1] != null)
                    {
                        nodes[y, x + 1].Next.Add(nodes[y + 1, x + 1]);
                        nodes[y + 1, x + 1].Prev = nodes[y, x + 1];
                    }
                }
            }
        }

        private void TraverseNode(Node n, int y, List<PointF> s, List<PointF> o)
        {
            bool noChildren = n.Next.Count == 0;
            n.Visited = true;

            AppendList(s, o, new PointF(n.MinX - 0.5f, y + 0.5f), new PointF(0, 0)); // TL
            AppendList(s, o, new PointF(n.MinX - 0.5f, y - 0.5f), new PointF(0, 0.001f)); // BL

            foreach (var sn in n.Next)
                TraverseNode(sn, y - 1, s, o);

            AppendList(s, o, new PointF(n.MaxX + 0.5f, y - 0.5f), new PointF(-0.001f, 0.001f)); // BR 
            AppendList(s, o, new PointF(n.MaxX + 0.5f, y + 0.5f), new PointF(-0.001f, 0)); // TR
        }

        private void AppendList(List<PointF> s, List<PointF> o, PointF p, PointF off)
        {
            // Scale and convert to mm
            p.X = (float)Math.Round(p.X * _scale / 1000.0, 3);
            p.Y = (float)Math.Round(p.Y * _scale / 1000.0, 3);

            // Flip
            if (_flip[0]) p.X = -p.X;
            if (_flip[1]) p.Y = -p.Y;

            if (s.Count == 0 || s[s.Count - 1] != p)
            {
                s.Add(p);
                o.Add(off);
            }
        }
    }
}
