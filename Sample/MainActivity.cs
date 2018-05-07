using Android.App;
using Android.Widget;
using Android.OS;
using AndroidPageCurl;
using Android.Graphics;
using Java.Lang;

namespace Sample
{
    [Activity(Label = "CurlView Sample", MainLauncher = true)]
    public class MainActivity : Activity
    {
        CurlView curlView;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);
            curlView = FindViewById<CurlView>(Resource.Id.curl);

            int index = 0;
            if (LastNonConfigurationInstance != null)
            {
                index = (int)LastNonConfigurationInstance;
            }

            curlView.PageProvider = new PageProvider(this);
            curlView.SizeChangedObserver = (new SizeChangedObserver(curlView));
            curlView.CurrentIndex = (index);
            //curlView.SetBackgroundColor(Color.White);

            // This is something somewhat experimental. Before uncommenting next
            // line, please see method comments in CurlView.
            curlView.EnableTouchPressure = true;

        }

        protected override void OnPause()
        {
            base.OnPause();
            curlView.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
            curlView.OnResume();
        }

        public override Object OnRetainNonConfigurationInstance()
        {
            return curlView.CurrentIndex;
        }

        class SizeChangedObserver : Java.Lang.Object, CurlView.ISizeChangedObserver
        {
            CurlView curlView;
            public SizeChangedObserver(CurlView curlView)
            {
                this.curlView = curlView;
            }

            public void OnSizeChanged(int width, int height)
            {
                if (width > height)
                {
                    curlView.ViewMode = CurlView.SHOW_TWO_PAGES;
                    curlView.SetMargins(.1f, .05f, .1f, .05f);
                }
                else
                {
                    curlView.ViewMode = CurlView.SHOW_ONE_PAGE;
                    curlView.SetMargins(.1f, .1f, .1f, .1f);
                }
            }
        }
    }


}

