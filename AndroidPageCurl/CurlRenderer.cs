using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;

namespace AndroidPageCurl
{
    public class CurlRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
    {
        // Constant for requesting left page rect.
        public static int PAGE_LEFT = 1;
        // Constant for requesting right page rect.
        public static int PAGE_RIGHT = 2;
        // Constants for changing view mode.
        public static int SHOW_ONE_PAGE = 1;
        public static int SHOW_TWO_PAGES = 2;
        // Set to true for checking quickly how perspective projection looks.
        private static bool USE_PERSPECTIVE_PROJECTION = false;
        // Background fill color.
        private int mBackgroundColor;
        // Curl meshes used for static and dynamic rendering.
        private List<CurlMesh> mCurlMeshes;
        private RectF mMargins = new RectF();
        private CurlRenderer.IObserver mObserver;
        // Page rectangles.
        private RectF mPageRectLeft;
        private RectF mPageRectRight;
        // View mode.
        private int mViewMode = SHOW_ONE_PAGE;
        // Screen size.
        private int mViewportWidth, mViewportHeight;
        // Rect for render area.
        private RectF mViewRect = new RectF();

        //Constructor
        public CurlRenderer(CurlRenderer.IObserver observer)
        {
            mObserver = observer;
            mCurlMeshes = new List<CurlMesh>();
            mPageRectLeft = new RectF();
            mPageRectRight = new RectF();
        }
        
        /// <summary>
        /// Adds CurlMesh to this renderer.
        /// </summary>
        /// <param name="mesh"></param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void AddCurlMesh(CurlMesh mesh)
        {
            RemoveCurlMesh(mesh);
            mCurlMeshes.Add(mesh);
        }

        /**
         * Returns rect reserved for left or right page. Value page should be
         * PAGE_LEFT or PAGE_RIGHT.
         */
        public RectF GetPageRect(int page)
        {
            if (page == PAGE_LEFT)
            {
                return mPageRectLeft;
            }
            else if (page == PAGE_RIGHT)
            {
                return mPageRectRight;
            }
            return null;
        }

        #region GLSurfaceView.IRenderer implementation
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void OnDrawFrame(IGL10 gl)
        {
            mObserver.OnDrawFrame();

            gl.GlClearColor(Color.GetRedComponent(mBackgroundColor) / 255f,
                    Color.GetGreenComponent(mBackgroundColor) / 255f,
                    Color.GetBlueComponent(mBackgroundColor) / 255f,
                    Color.GetAlphaComponent(mBackgroundColor) / 255f);
            gl.GlClear(GL10.GlColorBufferBit);
            gl.GlLoadIdentity();

            if (USE_PERSPECTIVE_PROJECTION)
            {
                gl.GlTranslatef(0, 0, -6f);
            }

            for (int i = 0; i < mCurlMeshes.Count; ++i)
            {
                mCurlMeshes.ElementAt(i).OnDrawFrame(gl);
            }
        }

        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            gl.GlViewport(0, 0, width, height);
            mViewportWidth = width;
            mViewportHeight = height;

            float ratio = (float)width / height;
            mViewRect.Top = 1.0f;
            mViewRect.Bottom = -1.0f;
            mViewRect.Left = -ratio;
            mViewRect.Right = ratio;
            UpdatePageRects();

            gl.GlMatrixMode(GL10.GlProjection);
            gl.GlLoadIdentity();
            if (USE_PERSPECTIVE_PROJECTION)
            {
                GLU.GluPerspective(gl, 20f, (float)width / height, .1f, 100f);
            }
            else
            {
                GLU.GluOrtho2D(gl, mViewRect.Left, mViewRect.Right,
                        mViewRect.Bottom, mViewRect.Top);
            }

            gl.GlMatrixMode(GL10.GlModelview);
            gl.GlLoadIdentity();
        }

        public void OnSurfaceCreated(IGL10 gl, Javax.Microedition.Khronos.Egl.EGLConfig config)
        {
            gl.GlClearColor(0f, 0f, 0f, 1f);
            gl.GlShadeModel(GL10.GlSmooth);
            gl.GlHint(GL10.GlPerspectiveCorrectionHint, GL10.GlNicest);
            gl.GlHint(GL10.GlLineSmoothHint, GL10.GlNicest);
            gl.GlHint(GL10.GlPolygonSmoothHint, GL10.GlNicest);
            gl.GlEnable(GL10.GlLineSmooth);
            gl.GlDisable(GL10.GlDepthTest);
            gl.GlDisable(GL10.GlCullFaceCapability);

            mObserver.OnSurfaceCreated();
        }
        #endregion

        //Removes CurlMesh from this renderer.
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RemoveCurlMesh(CurlMesh mesh)
        {
            while (mCurlMeshes.Remove(mesh))
                ;
        }

        //Change background/clear color.
        public int BackgroundColor
        {
            set { mBackgroundColor = value; }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetMargins(float left, float top, float right,
            float bottom)
        {
            mMargins.Left = left;
            mMargins.Top = top;
            mMargins.Right = right;
            mMargins.Bottom = bottom;
            UpdatePageRects();
        }

        /**
	    * Sets visible page count to one or two. Should be either SHOW_ONE_PAGE or
	    * SHOW_TWO_PAGES.
	    */
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetViewMode(int viewmode)
        {
            if (viewmode == SHOW_ONE_PAGE)
            {
                mViewMode = viewmode;
                UpdatePageRects();
            }
            else if (viewmode == SHOW_TWO_PAGES)
            {
                mViewMode = viewmode;
                UpdatePageRects();
            }
        }

        /// <summary>
        /// Translates screen coordinates into view coordinates.
        /// </summary>
        /// <param name="point"></param>
        public void translate(PointF point)
        {
            point.X = mViewRect.Left + (mViewRect.Width() * point.X / mViewportWidth);
            point.Y = mViewRect.Top - (-mViewRect.Height() * point.Y / mViewportHeight);
        }

        /// <summary>
        /// Recalculates page rectangles.
        /// </summary>
        private void UpdatePageRects()
        {
            if (mViewRect.Width() == 0 || mViewRect.Height() == 0)
            {
                return;
            }
            else if (mViewMode == SHOW_ONE_PAGE)
            {
                mPageRectRight.Set(mViewRect);
                mPageRectRight.Left += mViewRect.Width() * mMargins.Left;
                mPageRectRight.Right -= mViewRect.Width() * mMargins.Right;
                mPageRectRight.Top += mViewRect.Height() * mMargins.Top;
                mPageRectRight.Bottom -= mViewRect.Height() * mMargins.Bottom;

                mPageRectLeft.Set(mPageRectRight);
                mPageRectLeft.Offset(-mPageRectRight.Width(), 0);

                int bitmapW = (int)((mPageRectRight.Width() * mViewportWidth) / mViewRect
                        .Width());
                int bitmapH = (int)((mPageRectRight.Height() * mViewportHeight) / mViewRect
                        .Height());
                mObserver.OnPageSizeChanged(bitmapW, bitmapH);
            }
            else if (mViewMode == SHOW_TWO_PAGES)
            {
                mPageRectRight.Set(mViewRect);
                mPageRectRight.Left += mViewRect.Width() * mMargins.Left;
                mPageRectRight.Right -= mViewRect.Width() * mMargins.Right;
                mPageRectRight.Top += mViewRect.Height() * mMargins.Top;
                mPageRectRight.Bottom -= mViewRect.Height() * mMargins.Bottom;

                mPageRectLeft.Set(mPageRectRight);
                mPageRectLeft.Right = (mPageRectLeft.Right + mPageRectLeft.Left) / 2;
                mPageRectRight.Left = mPageRectLeft.Right;

                int bitmapW = (int)((mPageRectRight.Width() * mViewportWidth) / mViewRect
                        .Width());
                int bitmapH = (int)((mPageRectRight.Height() * mViewportHeight) / mViewRect
                        .Height());
                mObserver.OnPageSizeChanged(bitmapW, bitmapH);
            }
        }

        /// <summary>
        /// Observer for waiting render engine/state updates.
        /// </summary>
        public interface IObserver
        {
            /**
             * Called from onDrawFrame called before rendering is started. This is
             * intended to be used for animation purposes.
             */
            void OnDrawFrame();

            /**
             * Called once page size is changed. Width and height tell the page size
             * in pixels making it possible to update textures accordingly.
             */
            void OnPageSizeChanged(int width, int height);

            /**
             * Called from onSurfaceCreated to enable texture re-initialization etc
             * what needs to be done when this happens.
             */
            void OnSurfaceCreated();
        }
    }
}