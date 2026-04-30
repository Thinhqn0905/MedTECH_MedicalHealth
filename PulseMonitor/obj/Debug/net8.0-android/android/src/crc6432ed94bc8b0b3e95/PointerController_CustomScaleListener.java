package crc6432ed94bc8b0b3e95;


public class PointerController_CustomScaleListener
	extends android.view.ScaleGestureDetector.SimpleOnScaleGestureListener
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_onScale:(Landroid/view/ScaleGestureDetector;)Z:GetOnScale_Landroid_view_ScaleGestureDetector_Handler\n" +
			"";
		mono.android.Runtime.register ("LiveChartsCore.Native.PointerController+CustomScaleListener, LiveChartsCore.SkiaSharpView.Maui", PointerController_CustomScaleListener.class, __md_methods);
	}


	public PointerController_CustomScaleListener ()
	{
		super ();
		if (getClass () == PointerController_CustomScaleListener.class) {
			mono.android.TypeManager.Activate ("LiveChartsCore.Native.PointerController+CustomScaleListener, LiveChartsCore.SkiaSharpView.Maui", "", this, new java.lang.Object[] {  });
		}
	}


	public boolean onScale (android.view.ScaleGestureDetector p0)
	{
		return n_onScale (p0);
	}

	private native boolean n_onScale (android.view.ScaleGestureDetector p0);

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
