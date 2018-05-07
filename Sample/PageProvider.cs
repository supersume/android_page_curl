using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidPageCurl;

namespace Sample
{
    internal class PageProvider : AndroidPageCurl.CurlView.IPageProvider
    {
        // Bitmap resources.
        private int[] mBitmapIds = { Resource.Drawable.obama, Resource.Drawable.road_rage,
                Resource.Drawable.taipei_101, Resource.Drawable.world };

        Context context;

        public PageProvider(Context context)
        {
            this.context = context;
        }

        public int PageCount
        {
            get { return 5; }
        }

        public void UpdatePage(CurlPage page, int width, int height, int index)
        {
            Log.Debug("CurlView", $"Width: {width} Height: {height} Index: {index}");

            // First case is image on front side, solid colored back.
            if (index == 0)
            {
                Bitmap front = LoadBitmap(width, height, 0);
                page.SetTexture(front, CurlPage.SIDE_FRONT);
                page.SetColor(Color.Rgb(180, 180, 180), CurlPage.SIDE_BACK);
            }
            // Second case is image on back side, solid colored front.
            if (index == 1)
            {
                Bitmap back = LoadBitmap(width, height, 2);
                page.SetTexture(back, CurlPage.SIDE_BACK);
                page.SetColor(Color.Rgb(127, 140, 180), CurlPage.SIDE_FRONT);
            }
            // Third case is images on both sides.
            if (index == 2)
            {
                Bitmap front = LoadBitmap(width, height, 1);
                Bitmap back = LoadBitmap(width, height, 3);
                page.SetTexture(front, CurlPage.SIDE_FRONT);
                page.SetTexture(back, CurlPage.SIDE_BACK);
            }
            // Fourth case is images on both sides - plus they are blend against separate colors.
            if (index == 3)
            {
                Bitmap front = LoadBitmap(width, height, 2);
                Bitmap back = LoadBitmap(width, height, 1);
                page.SetTexture(front, CurlPage.SIDE_FRONT);
                page.SetTexture(back, CurlPage.SIDE_BACK);
                page.SetColor(Color.Argb(127, 170, 130, 255),
                        CurlPage.SIDE_FRONT);
                page.SetColor(Color.Rgb(255, 190, 150), CurlPage.SIDE_BACK);
            }
            // Fifth case is same image is assigned to front and back. In this
            // scenario only one texture is used and shared for both sides.
            if (index == 4)
            {
                Bitmap front = LoadBitmap(width, height, 0);
                page.SetTexture(front, CurlPage.SIDE_BOTH);
                page.SetColor(Color.Argb(127, 255, 255, 255),
                        CurlPage.SIDE_BACK);
            }
        }

        private Bitmap LoadBitmap(int width, int height, int index)
        {
            Bitmap b = Bitmap.CreateBitmap(width, height,
                    Bitmap.Config.Argb8888);
            unchecked
            {
                b.EraseColor(Color.White);
            }
            
            Canvas c = new Canvas(b);
            Drawable d = context.Resources.GetDrawable(mBitmapIds[index]);

            int margin = 7;
            int border = 3;
            Rect r = new Rect(margin, margin, width - margin, height - margin);

            int imageWidth = r.Width() - (border * 2);
            int imageHeight = imageWidth * d.IntrinsicHeight / d.IntrinsicWidth;
            if (imageHeight > r.Height() - (border * 2))
            {
                imageHeight = r.Height() - (border * 2);
                imageWidth = imageHeight * d.IntrinsicWidth / d.IntrinsicHeight;
            }

            r.Left += ((r.Width() - imageWidth) / 2) - border;
            r.Right = r.Left + imageWidth + border + border;
            r.Top += ((r.Height() - imageHeight) / 2) - border;
            r.Bottom = r.Top + imageHeight + border + border;

            Paint p = new Paint();
            p.Color = Color.White;
            c.DrawRect(r, p);
            r.Left += border;
            r.Right -= border;
            r.Top += border;
            r.Bottom -= border;

            d.Bounds = r;
            d.Draw(c);

            return b;
        }
    }
}