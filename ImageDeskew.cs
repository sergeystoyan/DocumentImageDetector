﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using System.Drawing;
using Emgu.CV.Features2D;

namespace testImageDetection
{
    class ImageDeskew
    {
        public static void Deskew(string imageFile)
        {
            ImageDeskew id = new ImageDeskew(imageFile);
            Bitmap b = id.DeskewAsColumnOfBlocks(1000, 40);
            MainForm.This.PageBox.Image = b;
        }

        public ImageDeskew(string imageFile)
        {
            image = new Image<Gray, byte>(imageFile);
            //MainForm.This.TemplateBox.Image = drawContours(template.GreyImage, template.CvContours);
        }
        public readonly Image<Gray, byte> image;

        public Bitmap DeskewAsSingleBlock()//good
        {
            return deskew(image)?.ToBitmap();
        }

        static Image<Gray, byte> deskew(Image<Gray, byte> image)//good
        {//https://becominghuman.ai/how-to-automatically-deskew-straighten-a-text-image-using-opencv-a0c30aed83df
            Image<Gray, byte> image2 = new Image<Gray, byte>(image.Size);
            CvInvoke.BitwiseNot(image, image2);
            CvInvoke.GaussianBlur(image2, image2, new Size(9, 9), 0);
            CvInvoke.Threshold(image2, image2, 0, 255, ThresholdType.Otsu | ThresholdType.Binary);
            Mat se = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(30, 5), new Point(-1, -1));
            CvInvoke.Dilate(image2, image2, se, new Point(-1, -1), 5, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);
            //Emgu.CV.CvInvoke.Erode(image, image, null, new Point(-1, -1), 1, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);
            //return image2;
            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Mat hierarchy = new Mat();
            CvInvoke.FindContours(image2, contours, hierarchy, RetrType.List, ChainApproxMethod.ChainApproxSimple);
            if (contours.Size < 1)
                return null;
            int maxW = 0;
            double angle = 0;
            for (int i = 0; i < contours.Size; i++)
            {
                RotatedRect rr = CvInvoke.MinAreaRect(contours[i]);
                int w = rr.MinAreaRect().Width;
                if (maxW < w)
                {
                    maxW = w;
                    angle = rr.Angle;
                }
            }
            if (angle > 45)
                angle -= 90;
            else if (angle < -45)
                angle += 90;
            RotationMatrix2D rotationMat = new RotationMatrix2D();
            CvInvoke.GetRotationMatrix2D(new PointF(0, 0), angle, 1, rotationMat);
            CvInvoke.WarpAffine(image, image2, rotationMat, image.Size);
            return image2;
        }

        public Bitmap DeskewAsColumnOfBlocks(int blockMaxHeight, int minBlockSpan)
        {
            //return image.ToBitmap();
            Image<Gray, byte> deskewedimage = new Image<Gray, byte>(image.Size);
            Image<Gray, byte> image2 = image.Clone();
            CvInvoke.BitwiseNot(image2, image2);
            //CvInvoke.Blur(image2, image2, new Size(3, 3), new Point(0, 0));
            CvInvoke.GaussianBlur(image2, image2, new Size(25, 25), 5);//remove small spots
            CvInvoke.Threshold(image2, image2, 125, 255, ThresholdType.Otsu | ThresholdType.Binary);
            Mat se = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(5, 5), new Point(-1, -1));
            CvInvoke.Dilate(image2, image2, se, new Point(-1, -1), 1, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);
            //CvInvoke.Erode(image2, image2, se, new Point(-1, -1), 5, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);

            //CvInvoke.BitwiseNot(image2, image2);
            //return image2.ToBitmap();

            VectorOfVectorOfPoint cs = new VectorOfVectorOfPoint();
            Mat h = new Mat();
            CvInvoke.FindContours(image2, cs, h, RetrType.Tree, ChainApproxMethod.ChainApproxSimple);
            if (cs.Size < 1)
                return null;

            Array hierarchy = h.GetData();
            List<Contour> contours = new List<Contour>();
            for (int i = 0; i < cs.Size; i++)
            {
                int p = (int)hierarchy.GetValue(0, i, Contour.HierarchyKey.Parent);
                if (p < 1)
                    contours.Add(new Contour(hierarchy, i, cs[i]));
            }
            if (contours.Where(a => a.ParentId < 0).Count() < 2)//the only parent is the whole page frame
                contours.RemoveAll(a => a.ParentId < 0);
            else
                contours.RemoveAll(a => a.ParentId >= 0);

            contours = contours.OrderBy(a => a.BoundingRectangle.Bottom).ToList();
            for (int blockY = 0; blockY < image.Height;)
            {
                int blockBottom = image.Height - 1;
                Tuple<Contour, Contour> lastSpan = null;
                for (; contours.Count > 0;)
                {
                    Contour c = contours[0];
                    contours.RemoveAt(0);
                    if (contours.Count > 0)
                    {
                        Contour minTop = contours.Aggregate((a, b) => a.BoundingRectangle.Top < b.BoundingRectangle.Top ? a : b);
                        if (c.BoundingRectangle.Bottom + minBlockSpan <= minTop.BoundingRectangle.Top)
                            lastSpan = new Tuple<Contour, Contour>(c, minTop);
                    }

                    if (c.BoundingRectangle.Bottom > blockY + blockMaxHeight && lastSpan != null)
                    {
                        blockBottom = lastSpan.Item1.BoundingRectangle.Bottom + minBlockSpan / 2;
                        break;
                    }
                }

                Rectangle blockRectangle = new Rectangle(0, blockY, image2.Width, blockBottom + 1 - blockY);
                Image<Gray, byte> blockImage = image.Copy(blockRectangle);
                blockImage = deskew(blockImage);
                deskewedimage.ROI = blockRectangle;
                blockImage.CopyTo(deskewedimage);
                deskewedimage.ROI = Rectangle.Empty;
                // break;
                blockY = blockBottom + 1;
            }
            return deskewedimage.ToBitmap();
        }

        //public Bitmap DeskewByBlocks(Size blockMaxSize, int minBlockSpan)//!!!not completed
        //{
        //    Image<Gray, byte> image2 = image.Clone();
        //    CvInvoke.BitwiseNot(image2, image2);
        //    //CvInvoke.Blur(image2, image2, new Size(3, 3), new Point(0, 0));
        //    CvInvoke.GaussianBlur(image2, image2, new Size(25, 25), 5);//remove small spots
        //    CvInvoke.Threshold(image2, image2, 125, 255, ThresholdType.Otsu | ThresholdType.Binary);
        //    Mat se = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(5, 5), new Point(-1, -1));
        //    CvInvoke.Dilate(image2, image2, se, new Point(-1, -1), 5, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);
        //    //CvInvoke.Erode(image2, image2, se, new Point(-1, -1), 5, BorderType.Constant, CvInvoke.MorphologyDefaultBorderValue);

        //    CvInvoke.BitwiseNot(image2, image2);
        //    //return image2.ToBitmap();

        //    VectorOfVectorOfPoint cs = new VectorOfVectorOfPoint();
        //    Mat h = new Mat();
        //    CvInvoke.FindContours(image2, cs, h, RetrType.Tree, ChainApproxMethod.ChainApproxSimple);
        //    if (cs.Size < 1)
        //        return null;

        //    Array hierarchy = h.GetData();
        //    List<Contour> contours = new List<Contour>();
        //    for (int i = 0; i < cs.Size; i++)
        //    {
        //        int p = (int)hierarchy.GetValue(0, i, Contour.HierarchyKey.Parent);
        //        if (p < 1)
        //            contours.Add(new Contour(hierarchy, i, cs[i]));
        //    }
        //    if (contours.Where(a => a.ParentId < 0).Count() < 2)//the only parent is the whole page frame
        //        contours.RemoveAll(a => a.ParentId < 0);
        //    else
        //        contours.RemoveAll(a => a.ParentId >= 0);

        //    int x = 0;
        //    int y = 0;
        //    for (Rectangle br = new Rectangle(-2, -2, -1, -1); ;)
        //    {
        //        x = br.Right + 1;
        //        if (x >= image.Width)
        //        {
        //            x = 0;
        //            y = br.Bottom + 1;
        //            if (y >= image.Height)
        //                break;
        //        }
        //        int blockWidth = blockMaxSize.Width;
        //        if (x + blockWidth > image.Width)
        //            blockWidth = image.Width - x;
        //        int blockHight = blockMaxSize.Height;
        //        if (y + blockHight > image.Height)
        //            blockHight = image.Height - y;
        //        br = new Rectangle(new Point(x, y), new Size(blockWidth, blockHight));

        //        int lastId;
        //        for (int j = 0; j < contours.Count; j++)
        //        {
        //            if (br.Contains(contours[j].BoundingRectangle))
        //            {
        //                contours.RemoveAt(j);
        //                j--;
        //            }

        //        }
        //        //deskew()
        //    }
        //    return image2.ToBitmap();
        //}
    }

    public class Contour
    {
        public Contour(Array hierarchy, int i, VectorOfPoint points)
        {
            I = i;
            Points = points;
            NextSiblingId = (int)hierarchy.GetValue(0, i, HierarchyKey.NextSibling);
            PreviousSiblingId = (int)hierarchy.GetValue(0, i, HierarchyKey.PreviousSibling);
            FirstChildId = (int)hierarchy.GetValue(0, i, HierarchyKey.FirstChild);
            ParentId = (int)hierarchy.GetValue(0, i, HierarchyKey.Parent);
        }
        public class HierarchyKey
        {
            public const int NextSibling = 0;
            public const int PreviousSibling = 1;
            public const int FirstChild = 2;
            public const int Parent = 3;
        }

        public readonly int I;
        public readonly VectorOfPoint Points;

        public readonly int NextSiblingId = 0;
        public readonly int PreviousSiblingId = 1;
        public readonly int FirstChildId = 2;
        public readonly int ParentId = 3;

        public float Angle
        {
            get
            {
                if (_Angle < -400)
                {
                    if (RotatedRect.Size.Width > RotatedRect.Size.Height)
                        _Angle = 90 + RotatedRect.Angle;
                    else
                        _Angle = RotatedRect.Angle;
                }
                return _Angle;
            }
        }
        float _Angle = -401;

        public PointF[] RotatedRectPoints
        {
            get
            {
                if (_RotatedRectPoints == null)
                    _RotatedRectPoints = RotatedRect.GetVertices();
                return _RotatedRectPoints;
            }
        }
        PointF[] _RotatedRectPoints = null;

        public RotatedRect RotatedRect
        {
            get
            {
                if (_RotatedRect.Size == RotatedRect.Empty.Size)
                    _RotatedRect = Emgu.CV.CvInvoke.FitEllipse(Points);
                return _RotatedRect;
            }
        }
        RotatedRect _RotatedRect = RotatedRect.Empty;

        public float Length
        {
            get
            {
                if (_Length < 0)
                    _Length = Math.Max(RotatedRect.Size.Width, RotatedRect.Size.Height);
                return _Length;
            }
        }
        float _Length = -1;

        public double Area
        {
            get
            {
                if (_Area < 0)
                    _Area = Emgu.CV.CvInvoke.ContourArea(Points);
                return _Area;
            }
        }
        double _Area = -1;

        public RectangleF MinAreaRectF
        {
            get
            {
                if (_MinAreaRectF == RectangleF.Empty)
                    _MinAreaRectF = RotatedRect.MinAreaRect();
                return _MinAreaRectF;
            }
        }
        RectangleF _MinAreaRectF = RectangleF.Empty;

        public Rectangle BoundingRectangle
        {
            get
            {
                if (_BoundingRectangle == Rectangle.Empty)
                    _BoundingRectangle = CvInvoke.BoundingRectangle(Points);
                return _BoundingRectangle;
            }
        }
        Rectangle _BoundingRectangle = Rectangle.Empty;

    }
}