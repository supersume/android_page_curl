using Android.Graphics;
using Android.Opengl;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using System;
using System.Runtime.CompilerServices;

namespace AndroidPageCurl
{
    /// <summary>
    /// Class implementing actual curl/page rendering.
    /// </summary>
    public class CurlMesh
    {
        // Flag for rendering some lines used for developing. Shows
        // curl position and one for the direction from the
        // position given. Comes handy once playing around with different
        // ways for following pointer.
        private static bool DRAW_CURL_POSITION = false;
        // Flag for drawing polygon outlines. Using this flag crashes on emulator
        // due to reason unknown to me. Leaving it here anyway as seeing polygon
        // outlines gives good insight how original rectangle is divided.
        private static bool DRAW_POLYGON_OUTLINES = false;
        // Flag for enabling shadow rendering.
        private static bool DRAW_SHADOW = true;
        // Flag for texture rendering. While this is likely something you
        // don't want to do it's been used for development purposes as texture
        // rendering is rather slow on emulator.
        private static bool DRAW_TEXTURE = true;

        // Colors for shadow. Inner one is the color drawn next to surface where
        // shadowed area starts and outer one is color shadow ends to.
        private static float[] SHADOW_INNER_COLOR = { 0f, 0f, 0f, .5f };
        private static float[] SHADOW_OUTER_COLOR = { 0f, 0f, 0f, .0f };

        // Let's avoid using 'new' as much as possible. Meaning we introduce arrays
        // once here and reuse them on runtime. Doesn't really have very much effect
        // but avoids some garbage collections from happening.
        private Array<ShadowVertex> mArrDropShadowVertices;
        private Array<Vertex> mArrIntersections;
        private Array<Vertex> mArrOutputVertices;
        private Array<Vertex> mArrRotatedVertices;
        private Array<Double> mArrScanLines;
        private Array<ShadowVertex> mArrSelfShadowVertices;
        private Array<ShadowVertex> mArrTempShadowVertices;
        private Array<Vertex> mArrTempVertices;

        // Buffers for feeding rasterizer.
        private FloatBuffer mBufColors;
        private FloatBuffer mBufCurlPositionLines;
        private FloatBuffer mBufShadowColors;
        private FloatBuffer mBufShadowVertices;
        private FloatBuffer mBufTexCoords;
        private FloatBuffer mBufVertices;

        private int mCurlPositionLinesCount;
        private int mDropShadowCount;

        // Boolean for 'flipping' texture sideways.
        private bool mFlipTexture = false;
        // Maximum number of split lines used for creating a curl.
        private int mMaxCurlSplits;

        // Bounding rectangle for this mesh. mRectagle[0] = top-left corner,
        // mRectangle[1] = bottom-left, mRectangle[2] = top-right and mRectangle[3]
        // bottom-right.
        private Vertex[] mRectangle = new Vertex[4];
        private int mSelfShadowCount;

        private bool mTextureBack = false;
        // Texture ids and other variables.
        private int[] mTextureIds = null;
        private CurlPage mTexturePage = new CurlPage();
        private RectF mTextureRectBack = new RectF();
        private RectF mTextureRectFront = new RectF();

        private int mVerticesCountBack;
        private int mVerticesCountFront;

        /// <summary>
        /// Maximum number curl can be divided into. The bigger the value
	    /// the smoother curl will be.With the cost of having more
	    /// polygons for drawing.
        /// </summary>
        /// <param name="maxCurlSplits"></param>
        public CurlMesh(int maxCurlSplits)
        {
            // There really is no use for 0 splits.
            mMaxCurlSplits = maxCurlSplits < 1 ? 1 : maxCurlSplits;

            mArrScanLines = new Array<Double>(maxCurlSplits + 2);
            mArrOutputVertices = new Array<Vertex>(7);
            mArrRotatedVertices = new Array<Vertex>(4);
            mArrIntersections = new Array<Vertex>(2);
            mArrTempVertices = new Array<Vertex>(7 + 4);
            for (int i = 0; i < 7 + 4; ++i)
            {
                mArrTempVertices.Add(new Vertex());
            }

            if (DRAW_SHADOW)
            {
                mArrSelfShadowVertices = new Array<ShadowVertex>(
                        (mMaxCurlSplits + 2) * 2);
                mArrDropShadowVertices = new Array<ShadowVertex>(
                        (mMaxCurlSplits + 2) * 2);
                mArrTempShadowVertices = new Array<ShadowVertex>(
                        (mMaxCurlSplits + 2) * 2);
                for (int i = 0; i < (mMaxCurlSplits + 2) * 2; ++i)
                {
                    mArrTempShadowVertices.Add(new ShadowVertex());
                }
            }

            // Rectangle consists of 4 vertices. Index 0 = top-left, index 1 =
            // bottom-left, index 2 = top-right and index 3 = bottom-right.
            for (int i = 0; i < 4; ++i)
            {
                mRectangle[i] = new Vertex();
            }
            // Set up shadow penumbra direction to each vertex. We do fake 'self
            // shadow' calculations based on this information.
            mRectangle[0].mPenumbraX = mRectangle[1].mPenumbraX = mRectangle[1].mPenumbraY = mRectangle[3].mPenumbraY = -1;
            mRectangle[0].mPenumbraY = mRectangle[2].mPenumbraX = mRectangle[2].mPenumbraY = mRectangle[3].mPenumbraX = 1;

            if (DRAW_CURL_POSITION)
            {
                mCurlPositionLinesCount = 3;
                ByteBuffer hvbb = ByteBuffer
                        .AllocateDirect(mCurlPositionLinesCount * 2 * 2 * 4);
                hvbb.Order(ByteOrder.NativeOrder());
                mBufCurlPositionLines = hvbb.AsFloatBuffer();
                mBufCurlPositionLines.Position(0);
            }

            // There are 4 vertices from bounding rect, max 2 from adding split line
            // to two corners and curl consists of max mMaxCurlSplits lines each
            // outputting 2 vertices.
            int maxVerticesCount = 4 + 2 + (2 * mMaxCurlSplits);
            ByteBuffer vbb = ByteBuffer.AllocateDirect(maxVerticesCount * 3 * 4);
            vbb.Order(ByteOrder.NativeOrder());
            mBufVertices = vbb.AsFloatBuffer();
            mBufVertices.Position(0);

            if (DRAW_TEXTURE)
            {
                ByteBuffer tbb = ByteBuffer
                        .AllocateDirect(maxVerticesCount * 2 * 4);
                tbb.Order(ByteOrder.NativeOrder());
                mBufTexCoords = tbb.AsFloatBuffer();
                mBufTexCoords.Position(0);
            }

            ByteBuffer cbb = ByteBuffer.AllocateDirect(maxVerticesCount * 4 * 4);
            cbb.Order(ByteOrder.NativeOrder());
            mBufColors = cbb.AsFloatBuffer();
            mBufColors.Position(0);

            if (DRAW_SHADOW)
            {
                int maxShadowVerticesCount = (mMaxCurlSplits + 2) * 2 * 2;
                ByteBuffer scbb = ByteBuffer
                        .AllocateDirect(maxShadowVerticesCount * 4 * 4);
                scbb.Order(ByteOrder.NativeOrder());
                mBufShadowColors = scbb.AsFloatBuffer();
                mBufShadowColors.Position(0);

                ByteBuffer sibb = ByteBuffer
                        .AllocateDirect(maxShadowVerticesCount * 3 * 4);
                sibb.Order(ByteOrder.NativeOrder());
                mBufShadowVertices = sibb.AsFloatBuffer();
                mBufShadowVertices.Position(0);

                mDropShadowCount = mSelfShadowCount = 0;
            }
        }

        /// <summary>
        /// Adds vertex to buffers.
        /// </summary>
        /// <param name="vertex"></param>
        private void AddVertex(Vertex vertex)
        {
            mBufVertices.Put((float)vertex.mPosX);
            mBufVertices.Put((float)vertex.mPosY);
            mBufVertices.Put((float)vertex.mPosZ);
            mBufColors.Put(vertex.mColorFactor * Color.GetRedComponent(vertex.mColor) / 255f);
            mBufColors.Put(vertex.mColorFactor * Color.GetGreenComponent(vertex.mColor) / 255f);
            mBufColors.Put(vertex.mColorFactor * Color.GetBlueComponent(vertex.mColor) / 255f);
            mBufColors.Put(Color.GetAlphaComponent(vertex.mColor) / 255f);
            if (DRAW_TEXTURE)
            {
                mBufTexCoords.Put((float)vertex.mTexX);
                mBufTexCoords.Put((float)vertex.mTexY);
            }
        }

        /// <summary>
        /// Sets curl for this mesh.
        /// </summary>
        /// <param name="curlPos">Position for curl 'center'. Can be any point on line collinear to curl.</param>
        /// <param name="curlDir">Curl direction, should be normalized.</param>
        /// <param name="radius">Radius of curl.</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Curl(PointF curlPos, PointF curlDir, double radius)
        {

            // First add some 'helper' lines used for development.
            if (DRAW_CURL_POSITION)
            {
                mBufCurlPositionLines.Position(0);

                mBufCurlPositionLines.Put(curlPos.X);
                mBufCurlPositionLines.Put(curlPos.Y - 1.0f);
                mBufCurlPositionLines.Put(curlPos.X);
                mBufCurlPositionLines.Put(curlPos.Y + 1.0f);
                mBufCurlPositionLines.Put(curlPos.X - 1.0f);
                mBufCurlPositionLines.Put(curlPos.Y);
                mBufCurlPositionLines.Put(curlPos.X + 1.0f);
                mBufCurlPositionLines.Put(curlPos.Y);

                mBufCurlPositionLines.Put(curlPos.X);
                mBufCurlPositionLines.Put(curlPos.Y);
                mBufCurlPositionLines.Put(curlPos.X + curlDir.X * 2);
                mBufCurlPositionLines.Put(curlPos.Y + curlDir.Y * 2);

                mBufCurlPositionLines.Position(0);
            }

            // Actual 'curl' implementation starts here.
            mBufVertices.Position(0);
            mBufColors.Position(0);
            if (DRAW_TEXTURE)
            {
                mBufTexCoords.Position(0);
            }

            // Calculate curl angle from direction.
            double curlAngle = Math.Acos(curlDir.X);
            curlAngle = curlDir.Y > 0 ? -curlAngle : curlAngle;

            // Initiate rotated rectangle which's is translated to curlPos and
            // rotated so that curl direction heads to right (1,0). Vertices are
            // ordered in ascending order based on x -coordinate at the same time.
            // And using y -coordinate in very rare case in which two vertices have
            // same x -coordinate.
            mArrTempVertices.AddAll(mArrRotatedVertices);
            mArrRotatedVertices.Clear();
            for (int i = 0; i < 4; ++i)
            {
                Vertex v = mArrTempVertices.Remove(0);
                v.Set(mRectangle[i]);
                v.Translate(-curlPos.X, -curlPos.Y);
                v.RotateZ(-curlAngle);
                int j = 0;
                for (; j < mArrRotatedVertices.Size(); ++j)
                {
                    Vertex v2 = mArrRotatedVertices.Get(j);
                    if (v.mPosX > v2.mPosX)
                    {
                        break;
                    }
                    if (v.mPosX == v2.mPosX && v.mPosY > v2.mPosY)
                    {
                        break;
                    }
                }
                mArrRotatedVertices.Add(j, v);
            }

            // Rotated rectangle lines/vertex indices. We need to find bounding
            // lines for rotated rectangle. After sorting vertices according to
            // their x -coordinate we don't have to worry about vertices at indices
            // 0 and 1. But due to inaccuracy it's possible vertex 3 is not the
            // opposing corner from vertex 0. So we are calculating distance from
            // vertex 0 to vertices 2 and 3 - and altering line indices if needed.
            // Also vertices/lines are given in an order first one has x -coordinate
            // at least the latter one. This property is used in getIntersections to
            // see if there is an intersection.
            int[][] lines = new int[][] { new int[]{ 0, 1 }, new int[] { 0, 2 }, new int[] { 1, 3 }, new int[] { 2, 3 } };
            {
                // TODO: There really has to be more 'easier' way of doing this -
                // not including extensive use of sqrt.
                Vertex v0 = mArrRotatedVertices.Get(0);
                Vertex v2 = mArrRotatedVertices.Get(2);
                Vertex v3 = mArrRotatedVertices.Get(3);
                double dist2 = Math.Sqrt((v0.mPosX - v2.mPosX)
                        * (v0.mPosX - v2.mPosX) + (v0.mPosY - v2.mPosY)
                        * (v0.mPosY - v2.mPosY));
                double dist3 = Math.Sqrt((v0.mPosX - v3.mPosX)
                        * (v0.mPosX - v3.mPosX) + (v0.mPosY - v3.mPosY)
                        * (v0.mPosY - v3.mPosY));
                if (dist2 > dist3)
                {
                    lines[1][1] = 3;
                    lines[2][1] = 2;
                }
            }

            mVerticesCountFront = mVerticesCountBack = 0;

            if (DRAW_SHADOW)
            {
                mArrTempShadowVertices.AddAll(mArrDropShadowVertices);
                mArrTempShadowVertices.AddAll(mArrSelfShadowVertices);
                mArrDropShadowVertices.Clear();
                mArrSelfShadowVertices.Clear();
            }

            // Length of 'curl' curve.
            double curlLength = Math.PI * radius;
            // Calculate scan lines.
            // TODO: Revisit this code one day. There is room for optimization here.
            mArrScanLines.Clear();
            if (mMaxCurlSplits > 0)
            {
                mArrScanLines.Add((double)0);
            }
            for (int i = 1; i < mMaxCurlSplits; ++i)
            {
                mArrScanLines.Add((-curlLength * i) / (mMaxCurlSplits - 1));
            }
            // As mRotatedVertices is ordered regarding x -coordinate, adding
            // this scan line produces scan area picking up vertices which are
            // rotated completely. One could say 'until infinity'.
            mArrScanLines.Add(mArrRotatedVertices.Get(3).mPosX - 1);

            // Start from right most vertex. Pretty much the same as first scan area
            // is starting from 'infinity'.
            double scanXmax = mArrRotatedVertices.Get(0).mPosX + 1;

            for (int i = 0; i < mArrScanLines.Size(); ++i)
            {
                // Once we have scanXmin and scanXmax we have a scan area to start
                // working with.
                double scanXmin = mArrScanLines.Get(i);
                // First iterate 'original' rectangle vertices within scan area.
                for (int j = 0; j < mArrRotatedVertices.Size(); ++j)
                {
                    Vertex v = mArrRotatedVertices.Get(j);
                    // Test if vertex lies within this scan area.
                    // TODO: Frankly speaking, can't remember why equality check was
                    // added to both ends. Guessing it was somehow related to case
                    // where radius=0f, which, given current implementation, could
                    // be handled much more effectively anyway.
                    if (v.mPosX >= scanXmin && v.mPosX <= scanXmax)
                    {
                        // Pop out a vertex from temp vertices.
                        Vertex n = mArrTempVertices.Remove(0);
                        n.Set(v);
                        // This is done solely for triangulation reasons. Given a
                        // rotated rectangle it has max 2 vertices having
                        // intersection.
                        Array<Vertex> intersections2 = GetIntersections(
                                mArrRotatedVertices, lines, n.mPosX);
                        // In a sense one could say we're adding vertices always in
                        // two, positioned at the ends of intersecting line. And for
                        // triangulation to work properly they are added based on y
                        // -coordinate. And this if-else is doing it for us.
                        if (intersections2.Size() == 1
                                && intersections2.Get(0).mPosY > v.mPosY)
                        {
                            // In case intersecting vertex is higher add it first.
                            mArrOutputVertices.AddAll(intersections2);
                            mArrOutputVertices.Add(n);
                        }
                        else if (intersections2.Size() <= 1)
                        {
                            // Otherwise add original vertex first.
                            mArrOutputVertices.Add(n);
                            mArrOutputVertices.AddAll(intersections2);
                        }
                        else
                        {
                            // There should never be more than 1 intersecting
                            // vertex. But if it happens as a fallback simply skip
                            // everything.
                            mArrTempVertices.Add(n);
                            mArrTempVertices.AddAll(intersections2);
                        }
                    }
                }

                // Search for scan line intersections.
                Array<Vertex> intersections = GetIntersections(mArrRotatedVertices,
                        lines, scanXmin);

                // We expect to get 0 or 2 vertices. In rare cases there's only one
                // but in general given a scan line intersecting rectangle there
                // should be 2 intersecting vertices.
                if (intersections.Size() == 2)
                {
                    // There were two intersections, add them based on y
                    // -coordinate, higher first, lower last.
                    Vertex v1 = intersections.Get(0);
                    Vertex v2 = intersections.Get(1);
                    if (v1.mPosY < v2.mPosY)
                    {
                        mArrOutputVertices.Add(v2);
                        mArrOutputVertices.Add(v1);
                    }
                    else
                    {
                        mArrOutputVertices.AddAll(intersections);
                    }
                }
                else if (intersections.Size() != 0)
                {
                    // This happens in a case in which there is a original vertex
                    // exactly at scan line or something went very much wrong if
                    // there are 3+ vertices. What ever the reason just return the
                    // vertices to temp vertices for later use. In former case it
                    // was handled already earlier once iterating through
                    // mRotatedVertices, in latter case it's better to avoid doing
                    // anything with them.
                    mArrTempVertices.AddAll(intersections);
                }

                // Add vertices found during this iteration to vertex etc buffers.
                while (mArrOutputVertices.Size() > 0)
                {
                    Vertex v = mArrOutputVertices.Remove(0);
                    mArrTempVertices.Add(v);

                    // Local texture front-facing flag.
                    bool textureFront;

                    // Untouched vertices.
                    if (i == 0)
                    {
                        textureFront = true;
                        mVerticesCountFront++;
                    }
                    // 'Completely' rotated vertices.
                    else if (i == mArrScanLines.Size() - 1 || curlLength == 0)
                    {
                        v.mPosX = -(curlLength + v.mPosX);
                        v.mPosZ = 2 * radius;
                        v.mPenumbraX = -v.mPenumbraX;

                        textureFront = false;
                        mVerticesCountBack++;
                    }
                    // Vertex lies within 'curl'.
                    else
                    {
                        // Even though it's not obvious from the if-else clause,
                        // here v.mPosX is between [-curlLength, 0]. And we can do
                        // calculations around a half cylinder.
                        double rotY = Math.PI * (v.mPosX / curlLength);
                        v.mPosX = radius * Math.Sin(rotY);
                        v.mPosZ = radius - (radius * Math.Cos(rotY));
                        v.mPenumbraX *= Math.Cos(rotY);
                        // Map color multiplier to [.1f, 1f] range.
                        v.mColorFactor = (float)(.1f + .9f * Math.Sqrt(Math
                                .Sin(rotY) + 1));

                        if (v.mPosZ >= radius)
                        {
                            textureFront = false;
                            mVerticesCountBack++;
                        }
                        else
                        {
                            textureFront = true;
                            mVerticesCountFront++;
                        }
                    }

                    // We use local textureFront for flipping backside texture
                    // locally. Plus additionally if mesh is in flip texture mode,
                    // we'll make the procedure "backwards". Also, until this point,
                    // texture coordinates are within [0, 1] range so we'll adjust
                    // them to final texture coordinates too.
                    if (textureFront != mFlipTexture)
                    {
                        v.mTexX *= mTextureRectFront.Right;
                        v.mTexY *= mTextureRectFront.Bottom;
                        v.mColor = mTexturePage.GetColor(CurlPage.SIDE_FRONT);
                    }
                    else
                    {
                        v.mTexX *= mTextureRectBack.Right;
                        v.mTexY *= mTextureRectBack.Bottom;
                        v.mColor = mTexturePage.GetColor(CurlPage.SIDE_BACK);
                    }

                    // Move vertex back to 'world' coordinates.
                    v.RotateZ(curlAngle);
                    v.Translate(curlPos.X, curlPos.Y);
                    AddVertex(v);

                    // Drop shadow is cast 'behind' the curl.
                    if (DRAW_SHADOW && v.mPosZ > 0 && v.mPosZ <= radius)
                    {
                        ShadowVertex sv = mArrTempShadowVertices.Remove(0);
                        sv.mPosX = v.mPosX;
                        sv.mPosY = v.mPosY;
                        sv.mPosZ = v.mPosZ;
                        sv.mPenumbraX = (v.mPosZ / 2) * -curlDir.X;
                        sv.mPenumbraY = (v.mPosZ / 2) * -curlDir.Y;
                        sv.mPenumbraColor = v.mPosZ / radius;
                        int idx = (mArrDropShadowVertices.Size() + 1) / 2;
                        mArrDropShadowVertices.Add(idx, sv);
                    }
                    // Self shadow is cast partly over mesh.
                    if (DRAW_SHADOW && v.mPosZ > radius)
                    {
                        ShadowVertex sv = mArrTempShadowVertices.Remove(0);
                        sv.mPosX = v.mPosX;
                        sv.mPosY = v.mPosY;
                        sv.mPosZ = v.mPosZ;
                        sv.mPenumbraX = ((v.mPosZ - radius) / 3) * v.mPenumbraX;
                        sv.mPenumbraY = ((v.mPosZ - radius) / 3) * v.mPenumbraY;
                        sv.mPenumbraColor = (v.mPosZ - radius) / (2 * radius);
                        int idx = (mArrSelfShadowVertices.Size() + 1) / 2;
                        mArrSelfShadowVertices.Add(idx, sv);
                    }
                }

                // Switch scanXmin as scanXmax for next iteration.
                scanXmax = scanXmin;
            }

            mBufVertices.Position(0);
            mBufColors.Position(0);
            if (DRAW_TEXTURE)
            {
                mBufTexCoords.Position(0);
            }

            // Add shadow Vertices.
            if (DRAW_SHADOW)
            {
                mBufShadowColors.Position(0);
                mBufShadowVertices.Position(0);
                mDropShadowCount = 0;

                for (int i = 0; i < mArrDropShadowVertices.Size(); ++i)
                {
                    ShadowVertex sv = mArrDropShadowVertices.Get(i);
                    mBufShadowVertices.Put((float)sv.mPosX);
                    mBufShadowVertices.Put((float)sv.mPosY);
                    mBufShadowVertices.Put((float)sv.mPosZ);
                    mBufShadowVertices.Put((float)(sv.mPosX + sv.mPenumbraX));
                    mBufShadowVertices.Put((float)(sv.mPosY + sv.mPenumbraY));
                    mBufShadowVertices.Put((float)sv.mPosZ);
                    for (int j = 0; j < 4; ++j)
                    {
                        double color = SHADOW_OUTER_COLOR[j]
                                + (SHADOW_INNER_COLOR[j] - SHADOW_OUTER_COLOR[j])
                                * sv.mPenumbraColor;
                        mBufShadowColors.Put((float)color);
                    }
                    mBufShadowColors.Put(SHADOW_OUTER_COLOR);
                    mDropShadowCount += 2;
                }
                mSelfShadowCount = 0;
                for (int i = 0; i < mArrSelfShadowVertices.Size(); ++i)
                {
                    ShadowVertex sv = mArrSelfShadowVertices.Get(i);
                    mBufShadowVertices.Put((float)sv.mPosX);
                    mBufShadowVertices.Put((float)sv.mPosY);
                    mBufShadowVertices.Put((float)sv.mPosZ);
                    mBufShadowVertices.Put((float)(sv.mPosX + sv.mPenumbraX));
                    mBufShadowVertices.Put((float)(sv.mPosY + sv.mPenumbraY));
                    mBufShadowVertices.Put((float)sv.mPosZ);
                    for (int j = 0; j < 4; ++j)
                    {
                        double color = SHADOW_OUTER_COLOR[j]
                                + (SHADOW_INNER_COLOR[j] - SHADOW_OUTER_COLOR[j])
                                * sv.mPenumbraColor;
                        mBufShadowColors.Put((float)color);
                    }
                    mBufShadowColors.Put(SHADOW_OUTER_COLOR);
                    mSelfShadowCount += 2;
                }
                mBufShadowColors.Position(0);
                mBufShadowVertices.Position(0);
            }
        }

        /// <summary>
        /// Calculates intersections for given scan line.
        /// </summary>
        /// <param name="vertices"></param>
        /// <param name="lineIndices"></param>
        /// <param name="scanX"></param>
        /// <returns></returns>
        private Array<Vertex> GetIntersections(Array<Vertex> vertices, int[][] lineIndices, double scanX)
        {
            mArrIntersections.Clear();
            // Iterate through rectangle lines each re-presented as a pair of
            // vertices.
            for (int j = 0; j < lineIndices.Length; j++)
            {
                Vertex v1 = vertices.Get(lineIndices[j][0]);
                Vertex v2 = vertices.Get(lineIndices[j][1]);
                // Here we expect that v1.mPosX >= v2.mPosX and wont do intersection
                // test the opposite way.
                if (v1.mPosX > scanX && v2.mPosX < scanX)
                {
                    // There is an intersection, calculate coefficient telling 'how
                    // far' scanX is from v2.
                    double c = (scanX - v2.mPosX) / (v1.mPosX - v2.mPosX);
                    Vertex n = mArrTempVertices.Remove(0);
                    n.Set(v2);
                    n.mPosX = scanX;
                    n.mPosY += (v1.mPosY - v2.mPosY) * c;
                    if (DRAW_TEXTURE)
                    {
                        n.mTexX += (v1.mTexX - v2.mTexX) * c;
                        n.mTexY += (v1.mTexY - v2.mTexY) * c;
                    }
                    if (DRAW_SHADOW)
                    {
                        n.mPenumbraX += (v1.mPenumbraX - v2.mPenumbraX) * c;
                        n.mPenumbraY += (v1.mPenumbraY - v2.mPenumbraY) * c;
                    }
                    mArrIntersections.Add(n);
                }
            }
            return mArrIntersections;
        }

        /// <summary>
        /// Texture page for this mesh.
        /// </summary>
        public CurlPage TexturePage
        {
            get { return mTexturePage; }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void OnDrawFrame(IGL10 gl)
        {
            // First allocate texture if there is not one yet.
            if (DRAW_TEXTURE && mTextureIds == null)
            {
                // Generate texture.
                mTextureIds = new int[2];
                gl.GlGenTextures(2, mTextureIds, 0);
                foreach (int textureId in mTextureIds)
                {
                    // Set texture attributes.
                    gl.GlBindTexture(GL10.GlTexture2d, textureId);
                    gl.GlTexParameterf(GL10.GlTexture2d,
                            GL10.GlTextureMinFilter, GL10.GlNearest);
                    gl.GlTexParameterf(GL10.GlTexture2d,
                            GL10.GlTextureMagFilter, GL10.GlNearest);
                    gl.GlTexParameterf(GL10.GlTexture2d, GL10.GlTextureWrapS,
                            GL10.GlClampToEdge);
                    gl.GlTexParameterf(GL10.GlTexture2d, GL10.GlTextureWrapT,
                            GL10.GlClampToEdge);
                }
            }

            if (DRAW_TEXTURE && mTexturePage.TexturesChanged)
            {
                gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[0]);
                Bitmap texture = mTexturePage.GetTexture(mTextureRectFront,
                        CurlPage.SIDE_FRONT);
                GLUtils.TexImage2D(GL10.GlTexture2d, 0, texture, 0);
                texture.Recycle();

                mTextureBack = mTexturePage.HasBackTexture;
                if (mTextureBack)
                {
                    gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[1]);
                    texture = mTexturePage.GetTexture(mTextureRectBack,
                            CurlPage.SIDE_BACK);
                    GLUtils.TexImage2D(GL10.GlTexture2d, 0, texture, 0);
                    texture.Recycle();
                }
                else
                {
                    mTextureRectBack.Set(mTextureRectFront);
                }

                mTexturePage.Recycle();
                Reset();
            }

            // Some 'global' settings.
            gl.GlEnableClientState(GL10.GlVertexArray);

            // TODO: Drop shadow drawing is done temporarily here to hide some
            // problems with its calculation.
            if (DRAW_SHADOW)
            {
                gl.GlDisable(GL10.GlTexture2d);
                gl.GlEnable(GL10.GlBlend);
                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlEnableClientState(GL10.GlColorArray);
                gl.GlColorPointer(4, GL10.GlFloat, 0, mBufShadowColors);
                gl.GlVertexPointer(3, GL10.GlFloat, 0, mBufShadowVertices);
                gl.GlDrawArrays(GL10.GlTriangleStrip, 0, mDropShadowCount);
                gl.GlDisableClientState(GL10.GlColorArray);
                gl.GlDisable(GL10.GlBlend);
            }

            if (DRAW_TEXTURE)
            {
                gl.GlEnableClientState(GL10.GlTextureCoordArray);
                gl.GlTexCoordPointer(2, GL10.GlFloat, 0, mBufTexCoords);
            }
            gl.GlVertexPointer(3, GL10.GlFloat, 0, mBufVertices);
            // Enable color array.
            gl.GlEnableClientState(GL10.GlColorArray);
            gl.GlColorPointer(4, GL10.GlFloat, 0, mBufColors);

            // Draw front facing blank vertices.
            gl.GlDisable(GL10.GlTexture2d);
            gl.GlDrawArrays(GL10.GlTriangleStrip, 0, mVerticesCountFront);

            // Draw front facing texture.
            if (DRAW_TEXTURE)
            {
                gl.GlEnable(GL10.GlBlend);
                gl.GlEnable(GL10.GlTexture2d);

                if (!mFlipTexture || !mTextureBack)
                {
                    gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[0]);
                }
                else
                {
                    gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[1]);
                }

                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlDrawArrays(GL10.GlTriangleStrip, 0, mVerticesCountFront);

                gl.GlDisable(GL10.GlBlend);
                gl.GlDisable(GL10.GlTexture2d);
            }

            int backStartIdx = Math.Max(0, mVerticesCountFront - 2);
            int backCount = mVerticesCountFront + mVerticesCountBack - backStartIdx;

            // Draw back facing blank vertices.
            gl.GlDrawArrays(GL10.GlTriangleStrip, backStartIdx, backCount);

            // Draw back facing texture.
            if (DRAW_TEXTURE)
            {
                gl.GlEnable(GL10.GlBlend);
                gl.GlEnable(GL10.GlTexture2d);

                if (mFlipTexture || !mTextureBack)
                {
                    gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[0]);
                }
                else
                {
                    gl.GlBindTexture(GL10.GlTexture2d, mTextureIds[1]);
                }

                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlDrawArrays(GL10.GlTriangleStrip, backStartIdx, backCount);

                gl.GlDisable(GL10.GlBlend);
                gl.GlDisable(GL10.GlTexture2d);
            }

            // Disable textures and color array.
            gl.GlDisableClientState(GL10.GlTextureCoordArray);
            gl.GlDisableClientState(GL10.GlColorArray);

            if (DRAW_POLYGON_OUTLINES)
            {
                gl.GlEnable(GL10.GlBlend);
                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlLineWidth(1.0f);
                gl.GlColor4f(0.5f, 0.5f, 1.0f, 1.0f);
                gl.GlVertexPointer(3, GL10.GlFloat, 0, mBufVertices);
                gl.GlDrawArrays(GL10.GlLineStrip, 0, mVerticesCountFront);
                gl.GlDisable(GL10.GlBlend);
            }

            if (DRAW_CURL_POSITION)
            {
                gl.GlEnable(GL10.GlBlend);
                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlLineWidth(1.0f);
                gl.GlColor4f(1.0f, 0.5f, 0.5f, 1.0f);
                gl.GlVertexPointer(2, GL10.GlFloat, 0, mBufCurlPositionLines);
                gl.GlDrawArrays(GL10.GlLines, 0, mCurlPositionLinesCount * 2);
                gl.GlDisable(GL10.GlBlend);
            }

            if (DRAW_SHADOW)
            {
                gl.GlEnable(GL10.GlBlend);
                gl.GlBlendFunc(GL10.GlSrcAlpha, GL10.GlOneMinusSrcAlpha);
                gl.GlEnableClientState(GL10.GlColorArray);
                gl.GlColorPointer(4, GL10.GlFloat, 0, mBufShadowColors);
                gl.GlVertexPointer(3, GL10.GlFloat, 0, mBufShadowVertices);
                gl.GlDrawArrays(GL10.GlTriangleStrip, mDropShadowCount,
                        mSelfShadowCount);
                gl.GlDisableClientState(GL10.GlColorArray);
                gl.GlDisable(GL10.GlBlend);
            }

            gl.GlDisableClientState(GL10.GlVertexArray);
        }

        /// <summary>
        /// Resets mesh to 'initial' state. Meaning this mesh will draw a plain
        /// textured rectangle after call to this method.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Reset()
        {
            mBufVertices.Position(0);
            mBufColors.Position(0);
            if (DRAW_TEXTURE)
            {
                mBufTexCoords.Position(0);
            }
            for (int i = 0; i < 4; ++i)
            {
                Vertex tmp = mArrTempVertices.Get(0);
                tmp.Set(mRectangle[i]);

                if (mFlipTexture)
                {
                    tmp.mTexX *= mTextureRectBack.Right;
                    tmp.mTexY *= mTextureRectBack.Bottom;
                    tmp.mColor = mTexturePage.GetColor(CurlPage.SIDE_BACK);
                }
                else
                {
                    tmp.mTexX *= mTextureRectFront.Right;
                    tmp.mTexY *= mTextureRectFront.Bottom;
                    tmp.mColor = mTexturePage.GetColor(CurlPage.SIDE_FRONT);
                }

                AddVertex(tmp);
            }
            mVerticesCountFront = 4;
            mVerticesCountBack = 0;
            mBufVertices.Position(0);
            mBufColors.Position(0);
            if (DRAW_TEXTURE)
            {
                mBufTexCoords.Position(0);
            }

            mDropShadowCount = mSelfShadowCount = 0;
        }

        /// <summary>
        /// Resets allocated texture id forcing creation of new one. After calling
        /// this method you most likely want to set bitmap too as it's lost. This
        /// method should be called only once e.g GL context is re-created as this
        /// method does not release previous texture id, only makes sure new one is
        /// requested on next render.
        /// </summary>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void ResetTexture()
        {
            mTextureIds = null;
        }

        /// <summary>
        /// If true, flips texture sideways.
        /// </summary>
        public bool FlipTexture
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            set
            {
                mFlipTexture = value;
                if (value)
                {
                    SetTextureCoords(1f, 0f, 0f, 1f);
                }
                else
                {
                    SetTextureCoords(0f, 0f, 1f, 1f);
                }
            }
        }

        /// <summary>
        /// Update mesh bounds.
        /// </summary>
        public RectF Rect
        {
            set
            {
                mRectangle[0].mPosX = value.Left;
                mRectangle[0].mPosY = value.Top;
                mRectangle[1].mPosX = value.Left;
                mRectangle[1].mPosY = value.Bottom;
                mRectangle[2].mPosX = value.Right;
                mRectangle[2].mPosY = value.Top;
                mRectangle[3].mPosX = value.Right;
                mRectangle[3].mPosY = value.Bottom;
            }
        }


        /// <summary>
        /// Sets texture coordinates to mRectangle vertices.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="top"></param>
        /// <param name="right"></param>
        /// <param name="bottom"></param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void SetTextureCoords(float left, float top, float right,
            float bottom)
        {
            mRectangle[0].mTexX = left;
            mRectangle[0].mTexY = top;
            mRectangle[1].mTexX = left;
            mRectangle[1].mTexY = bottom;
            mRectangle[2].mTexX = right;
            mRectangle[2].mTexY = top;
            mRectangle[3].mTexX = right;
            mRectangle[3].mTexY = bottom;
        }














        /// <summary>
        /// Simple fixed Size array implementation.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private class Array<T>
        {
            private Object[] mArray;
            private int mCapacity;
            private int mSize;

            public Array(int capacity)
            {
                mCapacity = capacity;
                mArray = new Object[capacity];
            }

            public void Add(int index, T item)
            {
                if (index < 0 || index > mSize || mSize >= mCapacity)
                {
                    throw new Java.Lang.IndexOutOfBoundsException();
                }
                for (int i = mSize; i > index; --i)
                {
                    mArray[i] = mArray[i - 1];
                }
                mArray[index] = item;
                ++mSize;
            }

            public void Add(T item)
            {
                if (mSize >= mCapacity)
                {
                    throw new Java.Lang.IndexOutOfBoundsException();
                }
                mArray[mSize++] = item;
            }

            public void AddAll(Array<T> array)
            {
                if (mSize + array.Size() > mCapacity)
                {
                    throw new Java.Lang.IndexOutOfBoundsException();
                }
                for (int i = 0; i < array.Size(); ++i)
                {
                    mArray[mSize++] = array.Get(i);
                }
            }

            public void Clear()
            {
                mSize = 0;
            }


            public T Get(int index)
            {
                if (index < 0 || index >= mSize)
                {
                    throw new Java.Lang.IndexOutOfBoundsException();
                }
                return (T)mArray[index];
            }

            public T Remove(int index)
            {
                if (index < 0 || index >= mSize)
                {
                    throw new Java.Lang.IndexOutOfBoundsException();
                }
                T item = (T)mArray[index];
                for (int i = index; i < mSize - 1; ++i)
                {
                    mArray[i] = mArray[i + 1];
                }
                --mSize;
                return item;
            }

            public int Size()
            {
                return mSize;
            }

        }

        /// <summary>
        /// Holder for shadow vertex information.
        /// </summary>
        private class ShadowVertex
        {
            public double mPenumbraColor;
            public double mPenumbraX;
            public double mPenumbraY;
            public double mPosX;
            public double mPosY;
            public double mPosZ;
        }

        /// <summary>
        /// Holder for vertex information.
        /// </summary>
        private class Vertex
        {
            public int mColor;
            public float mColorFactor;
            public double mPenumbraX;
            public double mPenumbraY;
            public double mPosX;
            public double mPosY;
            public double mPosZ;
            public double mTexX;
            public double mTexY;

            public Vertex()
            {
                mPosX = mPosY = mPosZ = mTexX = mTexY = 0;
                mColorFactor = 1.0f;
            }

            public void RotateZ(double theta)
            {
                double Cos = Math.Cos(theta);
                double Sin = Math.Sin(theta);
                double x = mPosX * Cos + mPosY * Sin;
                double y = mPosX * -Sin + mPosY * Cos;
                mPosX = x;
                mPosY = y;
                double px = mPenumbraX * Cos + mPenumbraY * Sin;
                double py = mPenumbraX * -Sin + mPenumbraY * Cos;
                mPenumbraX = px;
                mPenumbraY = py;
            }

            public void Set(Vertex vertex)
            {
                mPosX = vertex.mPosX;
                mPosY = vertex.mPosY;
                mPosZ = vertex.mPosZ;
                mTexX = vertex.mTexX;
                mTexY = vertex.mTexY;
                mPenumbraX = vertex.mPenumbraX;
                mPenumbraY = vertex.mPenumbraY;
                mColor = vertex.mColor;
                mColorFactor = vertex.mColorFactor;
            }

            public void Translate(double dx, double dy)
            {
                mPosX += dx;
                mPosY += dy;
            }
        }
    }
}