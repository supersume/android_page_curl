/*
   Copyright 2018 Asumege Alison
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at
       http://www.apache.org/licenses/LICENSE-2.0
   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace AndroidPageCurl
{
    /// <summary>
    /// Description: Storage class for page textures, blend colors and possibly some other values in the future.
    /// Author: Asumege Alison
    /// </summary>
    public class CurlPage
    {
        public const int SIDE_BACK = 2;
        public const int SIDE_BOTH = 3;
        public const int SIDE_FRONT = 1;

        private int mColorBack;
        private int mColorFront;
        private Bitmap mTextureBack;
        private Bitmap mTextureFront;
        private bool mTexturesChanged;

        public CurlPage()
        {
            Reset();
        }

        /// <summary>
        /// Get the color
        /// </summary>
        /// <param name="side"></param>
        /// <returns></returns>
        public int GetColor(int side)
        {
            switch (side)
            {
                case SIDE_FRONT:
                    return mColorFront;
                default:
                    return mColorBack;
            }
        }

        /// <summary>
        /// Calculates the next highest power of two for a given integer.
        /// </summary>
        /// <param name="n"></param>
        /// <returns></returns>
        private int GetNextHighestPO2(int n)
        {
            n -= 1;
            n = n | (n >> 1);
            n = n | (n >> 2);
            n = n | (n >> 4);
            n = n | (n >> 8);
            n = n | (n >> 16);
            n = n | (n >> 32);
            return n + 1;
        }

        /// <summary>
        /// Generates nearest power of two sized Bitmap for give Bitmap. Returns this
        /// new Bitmap using default return statement + original texture coordinates
        /// are stored into RectF.
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="textureRect"></param>
        /// <returns></returns>
        private Bitmap GetTexture(Bitmap bitmap, RectF textureRect)
        {
            // Bitmap original size.
            int w = bitmap.Width;
            int h = bitmap.Height;
            // Bitmap size expanded to next power of two. This is done due to
            // the requirement on many devices, texture width and height should
            // be power of two.
            int newW = GetNextHighestPO2(w);
            int newH = GetNextHighestPO2(h);

            // TODO: Is there another way to create a bigger Bitmap and copy
            // original Bitmap to it more efficiently? Immutable bitmap anyone?
            Bitmap bitmapTex = Bitmap.CreateBitmap(newW, newH, bitmap.GetConfig());
            Canvas c = new Canvas(bitmapTex);
            c.DrawBitmap(bitmap, 0, 0, null);

            // Calculate final texture coordinates.
            float texX = (float)w / newW;
            float texY = (float)h / newH;
            textureRect.Set(0f, 0f, texX, texY);

            return bitmapTex;
        }

        /// <summary>
        /// Getter for textures. Creates Bitmap sized to nearest power of two, copies
        /// original Bitmap into it and returns it. RectF given as parameter is
        /// filled with actual texture coordinates in this new upscaled texture
        /// Bitmap.
        /// </summary>
        /// <param name="textureRect"></param>
        /// <param name="side"></param>
        /// <returns></returns>
        public Bitmap GetTexture(RectF textureRect, int side)
        {
            switch (side)
            {
                case SIDE_FRONT:
                    return GetTexture(mTextureFront, textureRect);
                default:
                    return GetTexture(mTextureBack, textureRect);
            }
        }

        /// <summary>
        /// Returns true if textures have changed.
        /// </summary>
        public bool TexturesChanged
        {
            get { return mTexturesChanged; }
        }

        /// <summary>
        /// Returns true if textures have changed.
        /// </summary>
        public bool HasBackTexture
        {
            get { return !mTextureFront.Equals(mTextureBack); }
        }

        /// <summary>
        /// Recycles and frees underlying Bitmaps.
        /// </summary>
        public void Recycle()
        {
            if (mTextureFront != null)
            {
                mTextureFront.Recycle();
            }
            mTextureFront = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Rgb565);
            mTextureFront.EraseColor(mColorFront);
            if (mTextureBack != null)
            {
                mTextureBack.Recycle();
            }
            mTextureBack = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Rgb565);
            mTextureBack.EraseColor(mColorBack);
            mTexturesChanged = false;
        }

        /// <summary>
        /// Resets this CurlPage into its initial state.
        /// </summary>
        public void Reset()
        {
            mColorBack = Color.White;
            mColorFront = Color.White;
            Recycle();
        }

        /// <summary>
        /// Set blend color
        /// </summary>
        /// <param name="color"></param>
        /// <param name="side"></param>
        public void SetColor(int color, int side)
        {
            switch (side)
            {
                case SIDE_FRONT:
                    mColorFront = color;
                    break;
                case SIDE_BACK:
                    mColorBack = color;
                    break;
                default:
                    mColorFront = mColorBack = color;
                    break;
            }
        }

        /// <summary>
        /// Setter for textures.
        /// </summary>
        /// <param name="texture"></param>
        /// <param name="side"></param>
        public void SetTexture(Bitmap texture, int side)
        {
            if (texture == null)
            {
                texture = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Rgb565);
                if (side == SIDE_BACK)
                {
                    texture.EraseColor(mColorBack);
                }
                else
                {
                    texture.EraseColor(mColorFront);
                }
            }
            switch (side)
            {
                case SIDE_FRONT:
                    if (mTextureFront != null)
                        mTextureFront.Recycle();
                    mTextureFront = texture;
                    break;
                case SIDE_BACK:
                    if (mTextureBack != null)
                        mTextureBack.Recycle();
                    mTextureBack = texture;
                    break;
                case SIDE_BOTH:
                    if (mTextureFront != null)
                        mTextureFront.Recycle();
                    if (mTextureBack != null)
                        mTextureBack.Recycle();
                    mTextureFront = mTextureBack = texture;
                    break;
            }
            mTexturesChanged = true;
        }
    }
}