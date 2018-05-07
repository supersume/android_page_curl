using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Android.Util;
using Android.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AndroidPageCurl
{
    public class CurlView : GLSurfaceView, View.IOnTouchListener, CurlRenderer.IObserver
    {
        // Curl state. We are flipping none, left or right page.
        private const int CURL_LEFT = 1;
        private const int CURL_NONE = 0;
        private const int CURL_RIGHT = 2;

        // Constants for mAnimationTargetEvent.
        private static int SET_CURL_TO_LEFT = 1;
        private static int SET_CURL_TO_RIGHT = 2;

        // Shows one page at the center of view.
        public const int SHOW_ONE_PAGE = 1;
        // Shows two pages side by side.
        public const int SHOW_TWO_PAGES = 2;

        private bool mAllowLastPageCurl = true;

        private bool mAnimate = false;
        private long mAnimationDurationTime = 300;
        private PointF mAnimationSource = new PointF();
        private long mAnimationStartTime;
        private PointF mAnimationTarget = new PointF();
        private int mAnimationTargetEvent;

        private PointF mCurlDir = new PointF();

        private PointF mCurlPos = new PointF();
        private int mCurlState = CURL_NONE;
        // Current bitmap index. This is always showed as front of right page.
        private int mCurrentIndex = 0;

        // Start position for dragging.
        private PointF mDragStartPos = new PointF();

        private bool mEnableTouchPressure = false;
        // Bitmap size. These are updated from renderer once it's initialized.
        private int mPageBitmapHeight = -1;

        private int mPageBitmapWidth = -1;
        // Page meshes. Left and right meshes are 'static' while curl is used to
        // show page flipping.
        private CurlMesh mPageCurl;

        private CurlMesh mPageLeft;
        private IPageProvider mPageProvider;
        private CurlMesh mPageRight;

        private PointerPosition mPointerPos = new PointerPosition();

        private CurlRenderer mRenderer;
        private bool mRenderLeftPage = true;
        private ISizeChangedObserver mSizeChangedObserver;

        // One page is the default.
        private int mViewMode = SHOW_ONE_PAGE;

        #region Constructors
        public CurlView(Context context) : base(context)
        {
            Init(context);
        }

        public CurlView(Context context, IAttributeSet attrs) : base(context, attrs)
        {
            Init(context);
        }

        public CurlView(Context context, IAttributeSet attrs, int defStyle) : base(context, attrs)
        {
            Init(context);
        }

        /// <summary>
        /// Get or set current page index. Page indices are zero based values presenting page being shown on right side of the book.
        /// E.g if you set value to 4; right side front facing bitmap will be with index 4, back facing 5 and
        //  for left side page index 3 is front facing, and index 2 back facing(once page is on left side it's flipped over).
        /// </summary>
        public int CurrentIndex
        {
            get { return mCurrentIndex; }

            set
            {
                if (mPageProvider == null || value < 0)
                {
                    mCurrentIndex = 0;
                }
                else
                {
                    if (mAllowLastPageCurl)
                    {
                        mCurrentIndex = Math.Min(value, mPageProvider.PageCount);
                    }
                    else
                    {
                        mCurrentIndex = Math.Min(value,
                                mPageProvider.PageCount - 1);
                    }
                }
                UpdatePages();
                RequestRender();
            }
        }

        /// <summary>
        /// Initialize method.
        /// </summary>
        /// <param name="context"></param>
        private void Init(Context context)
        {
            mRenderer = new CurlRenderer(this);
            SetRenderer(mRenderer);
            RenderMode = Rendermode.WhenDirty;
            SetOnTouchListener(this);

            // Even though left and right pages are static we have to allocate room
            // for curl on them too as we are switching meshes. Another way would be
            // to swap texture ids only.
            mPageLeft = new CurlMesh(16);
            mPageRight = new CurlMesh(16);
            mPageCurl = new CurlMesh(16);
            mPageLeft.FlipTexture = true;
            mPageRight.FlipTexture = false;
        }


        #endregion

        #region CurlRenderer.IObserver implementation
        public void OnDrawFrame()
        {
            // We are not animating.
            if (mAnimate == false)
            {
                return;
            }

            long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            // If animation is done.
            if (currentTime >= mAnimationStartTime + mAnimationDurationTime)
            {
                if (mAnimationTargetEvent == SET_CURL_TO_RIGHT)
                {
                    // Switch curled page to right.
                    CurlMesh right = mPageCurl;
                    CurlMesh curl = mPageRight;
                    right.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                    right.FlipTexture = false;
                    right.Reset();
                    mRenderer.RemoveCurlMesh(curl);
                    mPageCurl = curl;
                    mPageRight = right;
                    // If we were curling left page update current index.
                    if (mCurlState == CURL_LEFT)
                    {
                        --mCurrentIndex;
                    }
                }
                else if (mAnimationTargetEvent == SET_CURL_TO_LEFT)
                {
                    // Switch curled page to left.
                    CurlMesh left = mPageCurl;
                    CurlMesh curl = mPageLeft;
                    left.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                    left.FlipTexture = (true);
                    left.Reset();
                    mRenderer.RemoveCurlMesh(curl);
                    if (!mRenderLeftPage)
                    {
                        mRenderer.RemoveCurlMesh(left);
                    }
                    mPageCurl = curl;
                    mPageLeft = left;
                    // If we were curling right page update current index.
                    if (mCurlState == CURL_RIGHT)
                    {
                        ++mCurrentIndex;
                    }
                }
                mCurlState = CURL_NONE;
                mAnimate = false;
                RequestRender();
            }
            else
            {
                mPointerPos.mPos.Set(mAnimationSource);
                float t = 1f - ((float)(currentTime - mAnimationStartTime) / mAnimationDurationTime);
                t = 1f - (t * t * t * (3 - 2 * t));
                mPointerPos.mPos.X += (mAnimationTarget.X - mAnimationSource.X) * t;
                mPointerPos.mPos.Y += (mAnimationTarget.Y - mAnimationSource.Y) * t;
                UpdateCurlPosition(mPointerPos);
            }
        }

        public void OnPageSizeChanged(int width, int height)
        {
            mPageBitmapWidth = width;
            mPageBitmapHeight = height;
            UpdatePages();
            RequestRender();
        }

        public void OnSurfaceCreated()
        {
            // In case surface is recreated, let page meshes drop allocated texture
            // ids and ask for new ones. There's no need to set textures here as
            // onPageSizeChanged should be called later on.
            mPageLeft.ResetTexture();
            mPageRight.ResetTexture();
            mPageCurl.ResetTexture();
        }
        #endregion

        protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
        {
            base.OnSizeChanged(width, height, oldWidth, oldHeight);
            RequestRender();
            if (mSizeChangedObserver != null)
            {
                mSizeChangedObserver.OnSizeChanged(width, height);
            }
        }

        #region View.IOnTouchListener implementation
        public bool OnTouch(View v, MotionEvent e)
        {
            // No dragging during animation at the moment.
            // TODO: Stop animation on touch event and return to drag mode.
            if (mAnimate || mPageProvider == null)
            {
                return false;
            }

            // We need page rects quite extensively so get them for later use.
            RectF rightRect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
            RectF leftRect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);

            // Store pointer position.
            mPointerPos.mPos.Set(e.GetX(), e.GetY());
            mRenderer.translate(mPointerPos.mPos);
            if (mEnableTouchPressure)
            {
                mPointerPos.mPressure = e.Pressure;
            }
            else
            {
                mPointerPos.mPressure = 0.8f;
            }

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    {

                        // Once we receive pointer down event its position is mapped to
                        // right or left edge of page and that'll be the position from where
                        // user is holding the paper to make curl happen.
                        mDragStartPos.Set(mPointerPos.mPos);

                        // First we make sure it's not over or below page. Pages are
                        // supposed to be same height so it really doesn't matter do we use
                        // left or right one.
                        if (mDragStartPos.Y > rightRect.Top)
                        {
                            mDragStartPos.Y = rightRect.Top;
                        }
                        else if (mDragStartPos.Y < rightRect.Bottom)
                        {
                            mDragStartPos.Y = rightRect.Bottom;
                        }

                        // Then we have to make decisions for the user whether curl is going
                        // to happen from left or right, and on which page.
                        if (mViewMode == SHOW_TWO_PAGES)
                        {
                            // If we have an open book and pointer is on the left from right
                            // page we'll mark drag position to left edge of left page.
                            // Additionally checking mCurrentIndex is higher than zero tells
                            // us there is a visible page at all.
                            if (mDragStartPos.X < rightRect.Left && mCurrentIndex > 0)
                            {
                                mDragStartPos.X = leftRect.Left;
                                StartCurl(CURL_LEFT);
                            }
                            // Otherwise check pointer is on right page's side.
                            else if (mDragStartPos.X >= rightRect.Left
                                    && mCurrentIndex < mPageProvider.PageCount)
                            {
                                mDragStartPos.X = rightRect.Right;
                                if (!mAllowLastPageCurl
                                        && mCurrentIndex >= mPageProvider.PageCount - 1)
                                {
                                    return false;
                                }
                                StartCurl(CURL_RIGHT);
                            }
                        }
                        else if (mViewMode == SHOW_ONE_PAGE)
                        {
                            float halfX = (rightRect.Right + rightRect.Left) / 2;
                            if (mDragStartPos.X < halfX && mCurrentIndex > 0)
                            {
                                mDragStartPos.X = rightRect.Left;
                                StartCurl(CURL_LEFT);
                            }
                            else if (mDragStartPos.X >= halfX
                                  && mCurrentIndex < mPageProvider.PageCount)
                            {
                                mDragStartPos.X = rightRect.Right;
                                if (!mAllowLastPageCurl
                                        && mCurrentIndex >= mPageProvider.PageCount - 1)
                                {
                                    return false;
                                }
                                StartCurl(CURL_RIGHT);
                            }
                        }
                        // If we have are in curl state, let this case clause flow through
                        // to next one. We have pointer position and drag position defined
                        // and this will create first render request given these points.
                        if (mCurlState == CURL_NONE)
                        {
                            return false;
                        }
                        break;
                    }
                case MotionEventActions.Move:
                    {
                        UpdateCurlPosition(mPointerPos);
                        break;
                    }
                case MotionEventActions.Cancel:
                    break;
                case MotionEventActions.Up:
                    {
                        if (mCurlState == CURL_LEFT || mCurlState == CURL_RIGHT)
                        {
                            // Animation source is the point from where animation starts.
                            // Also it's handled in a way we actually simulate touch events
                            // meaning the output is exactly the same as if user drags the
                            // page to other side. While not producing the best looking
                            // result (which is easier done by altering curl position and/or
                            // direction directly), this is done in a hope it made code a
                            // bit more readable and easier to maintain.
                            mAnimationSource.Set(mPointerPos.mPos);
                            mAnimationStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                            // Given the explanation, here we decide whether to simulate
                            // drag to left or right end.
                            if ((mViewMode == SHOW_ONE_PAGE && mPointerPos.mPos.X > (rightRect.Left + rightRect.Right) / 2)
                                    || mViewMode == SHOW_TWO_PAGES
                                    && mPointerPos.mPos.X > rightRect.Left)
                            {
                                // On right side target is always right page's right border.
                                mAnimationTarget.Set(mDragStartPos);
                                mAnimationTarget.X = mRenderer
                                        .GetPageRect(CurlRenderer.PAGE_RIGHT).Right;
                                mAnimationTargetEvent = SET_CURL_TO_RIGHT;
                            }
                            else
                            {
                                // On left side target depends on visible pages.
                                mAnimationTarget.Set(mDragStartPos);
                                if (mCurlState == CURL_RIGHT || mViewMode == SHOW_TWO_PAGES)
                                {
                                    mAnimationTarget.X = leftRect.Left;
                                }
                                else
                                {
                                    mAnimationTarget.X = rightRect.Left;
                                }
                                mAnimationTargetEvent = SET_CURL_TO_LEFT;
                            }
                            mAnimate = true;
                            RequestRender();
                        }
                        break;
                    }
            }

            return true;
        }
        #endregion

        /// <summary>
        /// Allow the last page to curl.
        /// </summary>
        public bool AllowLatPageCurl
        {
            set
            {
                mAllowLastPageCurl = value;
            }
        }

        /// <summary>
        /// Sets background color - or OpenGL clear color to be more precise. Color is a 32bit value consisting of 0xAARRGGBB and is extracted using
        /// Android.Graphics.Color eventually.
        /// </summary>
        /// <param name="color"></param>
        public override void SetBackgroundColor(Color color)
        {
            base.SetBackgroundColor(color);
            RequestRender();
        }

        /// <summary>
        /// Sets mPageCurl curl position.
        /// </summary>
        /// <param name="curlPosition"></param>
        /// <param name="curlDirection"></param>
        /// <param name="radius"></param>
        private void SetCurlPosition(PointF curlPosition, PointF curlDirection, double radius)
        {

            // First reposition curl so that page doesn't 'rip off' from book.
            if (mCurlState == CURL_RIGHT
                    || (mCurlState == CURL_LEFT && mViewMode == SHOW_ONE_PAGE))
            {
                RectF pageRect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                if (curlPosition.X >= pageRect.Right)
                {
                    mPageCurl.Reset();
                    RequestRender();
                    return;
                }
                if (curlPosition.X < pageRect.Left)
                {
                    curlPosition.X = pageRect.Left;
                }
                if (curlDirection.Y != 0)
                {
                    float diffX = curlPosition.X - pageRect.Left;
                    float leftY = curlPosition.Y + (diffX * curlDirection.X / curlDirection.Y);
                    if (curlDirection.Y < 0 && leftY < pageRect.Top)
                    {
                        curlDirection.X = curlPosition.Y - pageRect.Top;
                        curlDirection.Y = pageRect.Left - curlPosition.X;
                    }
                    else if (curlDirection.Y > 0 && leftY > pageRect.Bottom)
                    {
                        curlDirection.X = pageRect.Bottom - curlPosition.Y;
                        curlDirection.Y = curlPosition.X - pageRect.Left;
                    }
                }
            }
            else if (mCurlState == CURL_LEFT)
            {
                RectF pageRect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                if (curlPosition.X <= pageRect.Left)
                {
                    mPageCurl.Reset();
                    RequestRender();
                    return;
                }
                if (curlPosition.X > pageRect.Right)
                {
                    curlPosition.X = pageRect.Right;
                }
                if (curlDirection.Y != 0)
                {
                    float diffX = curlPosition.X - pageRect.Right;
                    float rightY = curlPosition.Y + (diffX * curlDirection.X / curlDirection.Y);
                    if (curlDirection.Y < 0 && rightY < pageRect.Top)
                    {
                        curlDirection.X = pageRect.Top - curlPosition.Y;
                        curlDirection.Y = curlPosition.X - pageRect.Right;
                    }
                    else if (curlDirection.Y > 0 && rightY > pageRect.Bottom)
                    {
                        curlDirection.X = curlPosition.Y - pageRect.Bottom;
                        curlDirection.Y = pageRect.Right - curlPosition.X;
                    }
                }
            }

            // Finally normalize direction vector and do rendering.
            double dist = Math.Sqrt(curlDirection.X * curlDirection.X + curlDirection.Y * curlDirection.Y);
            if (dist != 0)
            {
                curlDirection.X /= (float)dist;
                curlDirection.Y /= (float)dist;
                mPageCurl.Curl(curlPosition, curlDirection, radius);
            }
            else
            {
                mPageCurl.Reset();
            }

            RequestRender();
        }

        /// <summary>
        /// If set to true, touch event pressure information is used to adjust curl radius.The more you press, the flatter the curl becomes.This is
	    /// somewhat experimental and results may vary significantly between devices.
        /// On emulator pressure information seems to be flat 1.0f which is maximum
        /// value and therefore not very much of use.
        /// </summary>
        public bool EnableTouchPressure
        {
            set { mEnableTouchPressure = value; }
        }

        /// <summary>
        /// Set margins (or padding). Note: margins are proportional. Meaning a value of .1f will produce a 10% margin.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="top"></param>
        /// <param name="right"></param>
        /// <param name="bottom"></param>
        public void SetMargins(float left, float top, float right, float bottom)
        {
            mRenderer.SetMargins(left, top, right, bottom);
        }

        /// <summary>
        /// Get or set the Page Provider
        /// </summary>
        public IPageProvider PageProvider
        {
            get { return mPageProvider; }
            set { mPageProvider = value; }
        }

        /// <summary>
        /// Indicates whether left side page is rendered. This is useful mostly for
	    /// situations where right(main) page is aligned to left side of screen and
        /// left page is not visible anyway.
        /// </summary>
        public bool RenderLeftPage
        {
            set { mRenderLeftPage = value; }
        }

        /// <summary>
        /// SizeChangedObserver for this View. Call back method is called from this View's onSizeChanged method.
        /// </summary>
        public ISizeChangedObserver SizeChangedObserver
        {
            set { mSizeChangedObserver = value; }
        }

        public int ViewMode
        {
            set
            {
                switch (value)
                {
                    case SHOW_ONE_PAGE:
                        mViewMode = value;
                        mPageLeft.FlipTexture = true;
                        mRenderer.SetViewMode(CurlRenderer.SHOW_ONE_PAGE);
                        break;
                    case SHOW_TWO_PAGES:
                        mViewMode = value;
                        mPageLeft.FlipTexture = false;
                        mRenderer.SetViewMode(CurlRenderer.SHOW_TWO_PAGES);
                        break;
                }
            }
        }

        /// <summary>
        /// Switches meshes and loads new bitmaps if available. Updated to support 2 pages in landscape
        /// </summary>
        /// <param name="page"></param>
        private void StartCurl(int page)
        {
            switch (page)
            {

                // Once right side page is curled, first right page is assigned into
                // curled page. And if there are more bitmaps available new bitmap is
                // loaded into right side mesh.
                case CURL_RIGHT:
                    {
                        // Remove meshes from renderer.
                        mRenderer.RemoveCurlMesh(mPageLeft);
                        mRenderer.RemoveCurlMesh(mPageRight);
                        mRenderer.RemoveCurlMesh(mPageCurl);

                        // We are curling right page.
                        CurlMesh curl = mPageRight;
                        mPageRight = mPageCurl;
                        mPageCurl = curl;

                        if (mCurrentIndex > 0)
                        {
                            mPageLeft.FlipTexture = true;
                            mPageLeft.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                            mPageLeft.Reset();
                            if (mRenderLeftPage)
                            {
                                mRenderer.AddCurlMesh(mPageLeft);
                            }
                        }
                        if (mCurrentIndex < mPageProvider.PageCount - 1)
                        {
                            UpdatePage(mPageRight.TexturePage, mCurrentIndex + 1);
                            mPageRight.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                            mPageRight.FlipTexture = false;
                            mPageRight.Reset();
                            mRenderer.AddCurlMesh(mPageRight);
                        }

                        // Add curled page to renderer.
                        mPageCurl.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                        mPageCurl.FlipTexture = (false);
                        mPageCurl.Reset();
                        mRenderer.AddCurlMesh(mPageCurl);

                        mCurlState = CURL_RIGHT;
                        break;
                    }

                // On left side curl, left page is assigned to curled page. And if
                // there are more bitmaps available before currentIndex, new bitmap
                // is loaded into left page.
                case CURL_LEFT:
                    {
                        // Remove meshes from renderer.
                        mRenderer.RemoveCurlMesh(mPageLeft);
                        mRenderer.RemoveCurlMesh(mPageRight);
                        mRenderer.RemoveCurlMesh(mPageCurl);

                        // We are curling left page.
                        CurlMesh curl = mPageLeft;
                        mPageLeft = mPageCurl;
                        mPageCurl = curl;

                        if (mCurrentIndex > 1)
                        {
                            UpdatePage(mPageLeft.TexturePage, mCurrentIndex - 2);
                            mPageLeft.FlipTexture = true;
                            mPageLeft.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                            mPageLeft.Reset();
                            if (mRenderLeftPage)
                            {
                                mRenderer.AddCurlMesh(mPageLeft);
                            }
                        }

                        // If there is something to show on right page add it to renderer.
                        if (mCurrentIndex < mPageProvider.PageCount)
                        {
                            mPageRight.FlipTexture = false;
                            mPageRight.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                            mPageRight.Reset();
                            mRenderer.AddCurlMesh(mPageRight);
                        }

                        // How dragging previous page happens depends on view mode.
                        if (mViewMode == SHOW_ONE_PAGE
                                || (mCurlState == CURL_LEFT && mViewMode == SHOW_TWO_PAGES))
                        {
                            mPageCurl.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                            mPageCurl.FlipTexture = false;
                        }
                        else
                        {
                            mPageCurl.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                            mPageCurl.FlipTexture = (true);
                        }
                        mPageCurl.Reset();
                        mRenderer.AddCurlMesh(mPageCurl);

                        mCurlState = CURL_LEFT;
                        break;
                    }

            }
        }

        /// <summary>
        /// Updates curl position.
        /// </summary>
        /// <param name="pointerPos"></param>
        private void UpdateCurlPosition(PointerPosition pointerPos)
        {

            // Default curl radius.
            double radius = mRenderer.GetPageRect(CURL_RIGHT).Width() / 3;
            // TODO: This is not an optimal solution. Based on feedback received so
            // far; pressure is not very accurate, it may be better not to map
            // coefficient to range [0f, 1f] but something like [.2f, 1f] instead.
            // Leaving it as is until get my hands on a real device. On emulator
            // this doesn't work anyway.
            radius *= Math.Max(1f - pointerPos.mPressure, 0f);
            // NOTE: Here we set pointerPos to mCurlPos. It might be a bit confusing
            // later to see e.g "mCurlPos.X - mDragStartPos.X" used. But it's
            // actually pointerPos we are doing calculations against. Why? Simply to
            // optimize code a bit with the cost of making it unreadable. Otherwise
            // we had to this in both of the next if-else branches.
            mCurlPos.Set(pointerPos.mPos);

            // If curl happens on right page, or on left page on two page mode,
            // we'll calculate curl position from pointerPos.
            if (mCurlState == CURL_RIGHT
                    || (mCurlState == CURL_LEFT && mViewMode == SHOW_TWO_PAGES))
            {

                mCurlDir.X = mCurlPos.X - mDragStartPos.X;
                mCurlDir.Y = mCurlPos.Y - mDragStartPos.Y;
                float dist = (float)Math.Sqrt(mCurlDir.X * mCurlDir.X + mCurlDir.Y
                        * mCurlDir.Y);

                // Adjust curl radius so that if page is dragged far enough on
                // opposite side, radius gets closer to zero.
                float pageWidth = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT)
                        .Width();
                double curlLen = radius * Math.PI;
                if (dist > (pageWidth * 2) - curlLen)
                {
                    curlLen = Math.Max((pageWidth * 2) - dist, 0f);
                    radius = curlLen / Math.PI;
                }

                // Actual curl position calculation.
                if (dist >= curlLen)
                {
                    double translate = (dist - curlLen) / 2;
                    if (mViewMode == SHOW_TWO_PAGES)
                    {
                        mCurlPos.X -= mCurlDir.X * (float)translate / dist;
                    }
                    else
                    {
                        float pageLeftX = mRenderer
                                .GetPageRect(CurlRenderer.PAGE_RIGHT).Left;
                        radius = Math.Max(Math.Min(mCurlPos.X - pageLeftX, radius),
                                0f);
                    }
                    mCurlPos.Y -= mCurlDir.Y * (float)translate / dist;
                }
                else
                {
                    double angle = Math.PI * Math.Sqrt(dist / curlLen);
                    double translate = radius * Math.Sin(angle);
                    mCurlPos.X += mCurlDir.X * (float)translate / dist;
                    mCurlPos.Y += mCurlDir.Y * (float)translate / dist;
                }
            }
            // Otherwise we'll let curl follow pointer position.
            else if (mCurlState == CURL_LEFT)
            {

                // Adjust radius regarding how close to page edge we are.
                float pageLeftX = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT).Left;
                radius = Math.Max(Math.Min(mCurlPos.X - pageLeftX, radius), 0f);

                float pageRightX = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT).Right;
                mCurlPos.X -= Math.Min(pageRightX - mCurlPos.X, (float)radius);
                mCurlDir.X = mCurlPos.X + mDragStartPos.X;
                mCurlDir.Y = mCurlPos.Y - mDragStartPos.Y;
            }

            SetCurlPosition(mCurlPos, mCurlDir, radius);
        }

        /// <summary>
        /// Updates given CurlPage via PageProvider for page located at index.
        /// </summary>
        /// <param name="page"></param>
        /// <param name="index"></param>
        private void UpdatePage(CurlPage page, int index)
        {
            // First reset page to initial state.
            page.Reset();
            // Ask page provider to fill it up with bitmaps and colors.
            mPageProvider.UpdatePage(page, mPageBitmapWidth, mPageBitmapHeight,
                    index);
        }

        /// <summary>
        /// Updates bitmaps for page meshes.
        /// </summary>
        private void UpdatePages()
        {
            if (mPageProvider == null || mPageBitmapWidth <= 0
                    || mPageBitmapHeight <= 0)
            {
                return;
            }

            // Remove meshes from renderer.
            mRenderer.RemoveCurlMesh(mPageLeft);
            mRenderer.RemoveCurlMesh(mPageRight);
            mRenderer.RemoveCurlMesh(mPageCurl);

            int leftIdx = mCurrentIndex - 1;
            int rightIdx = mCurrentIndex;
            int curlIdx = -1;
            if (mCurlState == CURL_LEFT)
            {
                curlIdx = leftIdx;
                --leftIdx;
            }
            else if (mCurlState == CURL_RIGHT)
            {
                curlIdx = rightIdx;
                ++rightIdx;
            }

            if (rightIdx >= 0 && rightIdx < mPageProvider.PageCount)
            {
                UpdatePage(mPageRight.TexturePage, rightIdx);
                mPageRight.FlipTexture = false;
                mPageRight.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                mPageRight.Reset();
                mRenderer.AddCurlMesh(mPageRight);
            }
            if (leftIdx >= 0 && leftIdx < mPageProvider.PageCount)
            {
                UpdatePage(mPageLeft.TexturePage, leftIdx);
                mPageLeft.FlipTexture = true;
                mPageLeft.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                mPageLeft.Reset();
                if (mRenderLeftPage)
                {
                    mRenderer.AddCurlMesh(mPageLeft);
                }
            }
            if (curlIdx >= 0 && curlIdx < mPageProvider.PageCount)
            {
                UpdatePage(mPageCurl.TexturePage, curlIdx);

                if (mCurlState == CURL_RIGHT)
                {
                    mPageCurl.FlipTexture = true;
                    mPageCurl.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_RIGHT);
                }
                else
                {
                    mPageCurl.FlipTexture = false;
                    mPageCurl.Rect = mRenderer.GetPageRect(CurlRenderer.PAGE_LEFT);
                }

                mPageCurl.Reset();
                mRenderer.AddCurlMesh(mPageCurl);
            }
        }

        /// <summary>
        /// Provider for feeding 'book' with bitmaps which are used for rendering pages.
        /// </summary>
        public interface IPageProvider
        {
            /// <summary>
            /// Number of pages available.
            /// </summary>
            int PageCount { get; }

            /// <summary>
            /// Called once new bitmaps/textures are needed. Width and height are in 
            /// pixels telling the size it will be drawn on screen and following them
            /// ensures that aspect ratio remains. But it's possible to return bitmap 
            /// of any size though. You should use provided CurlPage for storing page 
            /// information for requested page number.
            /// </summary>
            /// <param name="page"></param>
            /// <param name="width"></param>
            /// <param name="height"></param>
            /// <param name="index">A number between 0 and BitmapCount - 1.</param>
            void UpdatePage(CurlPage page, int width, int height, int index);
        }

        /// <summary>
        /// Simple holder for pointer position.
        /// </summary>
        private class PointerPosition
        {
            public PointF mPos = new PointF();
            public float mPressure;
        }

        /// <summary>
        /// Observer interface for handling CurlView size changes.
        /// </summary>
        public interface ISizeChangedObserver
        {

            /// <summary>
            /// Called once CurlView size changes.
            /// </summary>
            /// <param name="width"></param>
            /// <param name="height"></param>
            void OnSizeChanged(int width, int height);
        }

    }
}
