//-----------------------------------------------------------------
//  Copyright 2011 Brady Wright and Above and Beyond Software
//	All rights reserved
//-----------------------------------------------------------------


using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <remarks>
/// A class that eases the process of placing objects
/// on-screen in a screen-relative, or object-relative
/// way, using pixels as units of distance.
/// </remarks>
[ExecuteInEditMode]
[System.Serializable]
[AddComponentMenu("EZ GUI/Utility/EZ Screen Placement")]
public class EZScreenPlacement : MonoBehaviour, IUseCamera
{
	
	/// <remarks>
	/// Which point of the text shares the position of the Transform.
	/// </remarks>
	public enum Anchor_Pos
	{
		Upper_Left,
		Upper_Center,
		Upper_Right,
		Middle_Left,
		Middle_Center,
		Middle_Right,
		Lower_Left,
		Lower_Center,
		Lower_Right
	}
	
	/// <summary>
	/// Specifies what the object will be aligned relative to on the horizontal axis.
	/// </summary>
	public enum HORIZONTAL_ALIGN
	{
		
		/// <summary>
		/// The object will not be repositioned along the X axis.
		/// </summary>
		NONE,

		/// <summary>
		/// The X coordinate of screenPos will be interpreted as the number of pixels from the left edge of the screen.
		/// </summary>
		SCREEN_LEFT,

		/// <summary>
		/// The X coordinate of screenPos will be interpreted as the number of pixels from the right edge of the screen.
		/// </summary>
		SCREEN_RIGHT,

		/// <summary>
		/// The X coordinate of screenPos will be interpreted as the number of pixels from the center of the screen.
		/// </summary>
		SCREEN_CENTER,

		/// <summary>
		/// The X coordinate of screenPos will be interpreted as the number of pixels from the object assigned to horizontalObj. i.e. negative values will place this object to the left of horizontalObj, and positive values to the right.
		/// </summary>
		OBJECT
		
	}

	/// <summary>
	/// Specifies what the object will be aligned relative to on the vertical axis.
	/// </summary>
	public enum VERTICAL_ALIGN
	{
		
		/// <summary>
		/// The object will not be repositioned along the Y axis.
		/// </summary>
		NONE,

		/// <summary>
		/// The Y coordinate of screenPos will be interpreted as the number of pixels from the top edge of the screen.
		/// </summary>
		SCREEN_TOP,

		/// <summary>
		/// The Y coordinate of screenPos will be interpreted as the number of pixels from the bottom edge of the screen.
		/// </summary>
		SCREEN_BOTTOM,

		/// <summary>
		/// The Y coordinate of screenPos will be interpreted as the number of pixels from the center of the screen.
		/// </summary>
		SCREEN_CENTER,

		/// <summary>
		/// The Y coordinate of screenPos will be interpreted as the number of pixels from the object assigned to verticalObj. i.e. negative values will place this object above verticalObj, and positive values below.
		/// </summary>
		OBJECT
		
	}

	[System.Serializable]
	public class RelativeTo
	{
		public HORIZONTAL_ALIGN horizontal = HORIZONTAL_ALIGN.SCREEN_LEFT;
		public VERTICAL_ALIGN vertical = VERTICAL_ALIGN.SCREEN_TOP;

		// The script that contains this object
		protected EZScreenPlacement script;

		public EZScreenPlacement Script
		{
			get { return script; }
			set { Script = value; }
		}

		public bool Equals(RelativeTo rt)
		{
			if (rt == null)
				return false;
			return (horizontal == rt.horizontal && vertical == rt.vertical);
		}

		public void Copy(RelativeTo rt)
		{
			if (rt == null)
				return;
			horizontal = rt.horizontal;
			vertical = rt.vertical;
		}

		// Copy constructor
		public RelativeTo(EZScreenPlacement sp, RelativeTo rt)
		{
			script = sp;
			Copy(rt);
		}

		public RelativeTo(EZScreenPlacement sp)
		{
			script = sp;
		}
	}

	/// <summary>
	/// The camera with which this object should be positioned.
	/// </summary>
	public Camera renderCamera;

	/// <summary>
	/// The position of the object, relative to the screen or other object.
	/// </summary>
	public Vector3 screenPos = Vector3.forward;
	
	public Vector3 minScreenPos = Vector3.zero;
	
	/// <summary>
	/// When set to true, the Z component of Screen Pos will be calculated
	/// based upon the distance of the object to the render camera, rather
	/// than controlling the distance to the render camera.  Enable this
	/// option, for example, when you want to preserve parent-relative
	/// positioning on the Z-axis.
	/// </summary>
	public bool ignoreZ = false;

	/// <summary>
	/// Settings object that describes what this object is positioned
	/// relative to.
	/// </summary>
	public RelativeTo relativeTo;

	/// <summary>
	/// The object to which this object is relative.
	/// NOTE: Only used if either the vertical or horizontal elements 
	/// of relativeTo are set to OBJECT (or both).
	/// </summary>
	public EZScreenPlacement relativeObject;
	
	Vector3 RelativeAnchorPoint
	{
		
		get	
		{

			if (relativeObject != null)
			{
				
				if (relativeObject.GetComponent<Renderer>() != null)
				{
						
					Vector3 center = relativeObject.GetComponent<Renderer>().bounds.center;
					
					float width = relativeObject.GetComponent<Renderer>().bounds.size.x;
					float height = relativeObject.GetComponent<Renderer>().bounds.size.y;
	
					switch (relativeAnchor)
					{
						
						case Anchor_Pos.Lower_Center:
							
							return new Vector3(center.x, center.y - (height/2), center.z);
							
						case Anchor_Pos.Lower_Left:
							
							return new Vector3(center.x - (width / 2), center.y - (height/2), center.z);
							
						case Anchor_Pos.Lower_Right:
							
							return new Vector3(center.x + (width / 2), center.y - (height/2), center.z);
		
						case Anchor_Pos.Middle_Center:
							
							return new Vector3(center.x, center.y, center.z);
		
						case Anchor_Pos.Middle_Left:
							
							return new Vector3(center.x - (width / 2), center.y, center.z);
							
						case Anchor_Pos.Middle_Right:
							
							return new Vector3(center.x + (width / 2), center.y, center.z);
			
						case Anchor_Pos.Upper_Center:
							
							return new Vector3(center.x, center.y + (height/2), center.z);
							
						case Anchor_Pos.Upper_Left:
							
							return new Vector3(center.x - (width / 2), center.y + (height/2), center.z);
							
						case Anchor_Pos.Upper_Right:
							
							return new Vector3(center.x + (width / 2), center.y + (height/2), center.z);
										
					}
					
				}
				else
				{
					
					return relativeObject.transform.position;
					
				}
				
			}
			
			return Vector3.zero;
			
		}
		
		
		
	}

	
	/// <summary>
	/// Where should the object anchor from? 
	/// </summary>
	public Anchor_Pos relativeAnchor;

	/// <summary>
	/// When checked, you can simply use the transform handles in the scene view
	/// to roughly position your object in the scene, and the appropriate
	/// values will be automatically calculated for Screen Pos based on your
	/// other settings.
	/// If you're having problems with slight rounding errors making small
	/// changes to your Screen Pos coordinate, you should probably uncheck
	/// this option.  This is particularly likely to happen if your camera
	/// is rotated at all.
	/// </summary>
	public bool allowTransformDrag = false;
    
    public bool iPhoneXOffsetX;
    public bool iPhoneXOffsetY;
    public static bool DeviceRequiresSafeArea {
		get {

#if UNITY_EDITOR || UNITY_STANDALONE
            // editor has inconsistent reports between screen res and player window size, so to prevent ui breaking we force no notch
            return false;
#else
            return Screen.safeArea.width < Screen.currentResolution.width || Screen.safeArea.height < Screen.currentResolution.height;
#endif

        }
    }

    public static int SafeAreaLeftOffset
    {
        get
        {
            return (int)Screen.safeArea.x;
        }
    }

    public static int SafeAreaRightOffset
    {
        get
        {
            return (int)(Screen.safeArea.x + Screen.safeArea.width - Screen.currentResolution.width);
        }
    }
    
    public static int SafeAreaTopOffset
    {
        get
        {
            return (int)(Screen.currentResolution.height - (Screen.safeArea.y + Screen.safeArea.height));
        }
    }

    public static int SafeAreaBottomOffset
    {
        get
        {
            return (int)Screen.safeArea.y;
        }
    }

    protected Vector2 screenSize;
	
	bool isSceneObject;

	public bool IsSceneObject { get { return isSceneObject; } }

#if (UNITY_3_0 || UNITY_3_1 || UNITY_3_2 || UNITY_3_3 || UNITY_3_4 || UNITY_3_5 || UNITY_3_6 || UNITY_3_7 || UNITY_3_8 || UNITY_3_9) && UNITY_EDITOR
	[System.NonSerialized]
	protected bool justEnabled = true;	// Helps us get around a silly issue that, sometimes when an object has OnEnabled() called, it may call SetCamera() on us in edit mode, and this happens in response to an OnGUI() call in-editor, meaning we'll get invalid camera information as a result, thereby positioning the object in the wrong place, so we need to detect this and just ignore any such SetCamera() call.
	[System.NonSerialized]
	protected EZScreenPlacementMirror mirror = new EZScreenPlacementMirror();
#else
	[System.NonSerialized]
	protected bool justEnabled = true;	// Helps us get around a silly issue that, sometimes when an object has OnEnabled() called, it may call SetCamera() on us in edit mode, and this happens in response to an OnGUI() call in-editor, meaning we'll get invalid camera information as a result, thereby positioning the object in the wrong place, so we need to detect this and just ignore any such SetCamera() call.
	[System.NonSerialized]
	protected EZScreenPlacementMirror mirror = new EZScreenPlacementMirror();
#endif

	protected bool m_awake = false;
	protected bool m_started = false;

	void Awake()
	{

		if (Application.isPlaying)
			isSceneObject = true;
		
		if (m_awake)
			return;

		m_awake = true;

		IUseCamera uc = (IUseCamera) GetComponent("IUseCamera");
		if (uc != null)
			renderCamera = uc.RenderCamera;

		if (renderCamera == null)
			renderCamera = Camera.main;

		if (relativeTo == null)
			relativeTo = new RelativeTo(this);
		else if (relativeTo.Script != this)
		{
			// This appears to be a duplicate object,
			// so create our own copy of the relativeTo:
			RelativeTo newRT = new RelativeTo(this, relativeTo);
			relativeTo = newRT;
		}
	}

	Coroutine coroutine;
	
	public void Start()
	{

		if (renderCamera != null)
		{
			screenSize.x = renderCamera.pixelWidth;
			screenSize.y = renderCamera.pixelHeight;
		}
		else
        {
			screenSize.x = Screen.width;
			screenSize.y = Screen.height;
        }

		if (m_started)
			return;
		
		m_started = true;

		PositionOnScreenRecursively();

		DoMirror();

	}

	void OnEnable()
	{
		StartCoroutine(PositionDelayed ());
	}

	IEnumerator PositionDelayed()
	{
		yield return null;
		PositionOnScreenRecursively ();
	}

	int framePositioned = -1;

	public bool positionedThisFrame
	{
		
		get { return framePositioned == Time.frameCount; }
	
	}
	
	public void PositionOnScreenRecursively()
	{
		
		if (relativeObject != null)
		{
		
			relativeObject.PositionOnScreenRecursively();
				
		}
		
		PositionOnScreen();
			
	}
	
	float MaxMin(float val1, float val2, int sign)
	{
	
		if (sign >= 0)
		{
			return Mathf.Max(val1,val2);
		}
		
		else if (sign < 0)
		{
			
			return Mathf.Min(val1,val2);
			
		}
		else
		{
			
			return 0;
			
		}
		
		
	}
	
    /// <summary>
	/// Calculates the world position from a given screen or object-relative position,
	/// according to the current screen-space settings.
    /// </summary>
    /// <param name="screenPos">The screen/object-relative position</param>
    /// <returns>The corresponding position in world space.</returns>
    public Vector3 ScreenPosToWorldPos(Vector3 targetPos) 
	{
		if (!m_started)
			Start();

		if (renderCamera == null)
		{
			//Debug.LogError("Render camera not yet assigned to EZScreenPlacement component of \"" + name + "\" when attempting to call PositionOnScreen()");
			return transform.position;
		}
		
		Vector3 curPos = renderCamera.WorldToScreenPoint(transform.position);
		Vector3 pos = targetPos;
		Vector3 min = minScreenPos;
		
		Vector2 size = screenSize;
		
		if (relativeObject != null)
		{
			
			SpriteRoot root = relativeObject.GetComponent<SpriteRoot>();
			
			if (root != null)
			{
	
				if (this.relativeTo.horizontal == HORIZONTAL_ALIGN.OBJECT)
				{
					size.x = root.PixelSize.x;
				}
				
				if (this.relativeTo.vertical == VERTICAL_ALIGN.OBJECT)
				{
					size.y = root.PixelSize.y;
				}
				
			}
			
		}
		

		pos.x = (int) ((pos.x/100)*size.x);
		pos.y = (int) ((pos.y/100)*size.y);

        min.x = (int) ((min.x/100)*size.x);
		min.y = (int) ((min.y/100)*size.y);
		
		int xSign = (int) Mathf.Sign(targetPos.x);
		int ySign = (int) Mathf.Sign(targetPos.y);
		
		pos.x = MaxMin(pos.x,min.x,xSign);
		pos.y = MaxMin(pos.y,min.y,ySign);
		
		if (this.transform.localScale == Vector3.zero)
		{
			
			pos = Vector2.zero;
			min = Vector2.zero;
			
		}
		
		switch (relativeTo.horizontal)
		{
			case HORIZONTAL_ALIGN.SCREEN_RIGHT:
				pos.x = size.x + MaxMin(pos.x,min.x,xSign);
				break;
			case HORIZONTAL_ALIGN.SCREEN_CENTER:
				pos.x = size.x * 0.5f + MaxMin(pos.x,min.x,xSign);
				break;
			case HORIZONTAL_ALIGN.OBJECT:
			//TO DO: Need to fix the offset when taking a screenshot using the screenshot tool 
			//INSPECTOR HACK: Position the UI first while having the relative object reference on it, after positioning, change the relative object reference to none.
				if (relativeObject != null)
				{
					pos.x = renderCamera.WorldToScreenPoint(RelativeAnchorPoint).x + MaxMin(pos.x,min.x,xSign);
				}
				else
				{
					pos.x = curPos.x;
				
				}
			
				break;
			case HORIZONTAL_ALIGN.NONE:
				pos.x = curPos.x;
				break;
		}

		switch (relativeTo.vertical)
		{
			case VERTICAL_ALIGN.SCREEN_TOP:
				pos.y = size.y + MaxMin(pos.y,min.y,ySign);
				break;
			case VERTICAL_ALIGN.SCREEN_CENTER:
				pos.y = size.y * 0.5f + MaxMin(pos.y,min.y,ySign);
				break;
			case VERTICAL_ALIGN.OBJECT:
				if (relativeObject != null)
				{
					pos.y = renderCamera.WorldToScreenPoint(RelativeAnchorPoint).y + MaxMin(pos.y,min.y,ySign);

				}
				else
					pos.y = curPos.y;
				break;
			case VERTICAL_ALIGN.NONE:
				pos.y = curPos.y;
				break;
		}

        // Apply offsets for notches
        if (DeviceRequiresSafeArea)
        {

            if (iPhoneXOffsetX)
            {
                if (this.relativeTo.horizontal == HORIZONTAL_ALIGN.SCREEN_LEFT)
                    pos.x += SafeAreaLeftOffset;
                else if (this.relativeTo.horizontal == HORIZONTAL_ALIGN.SCREEN_RIGHT)
                    pos.x += SafeAreaRightOffset;
            }

            if (iPhoneXOffsetY)
            {
                if (this.relativeTo.vertical == VERTICAL_ALIGN.SCREEN_BOTTOM)
                    pos.y += SafeAreaBottomOffset;
            }

        }

        return renderCamera.ScreenToWorldPoint(pos);
	}
	public static bool screenshot;
	/// <summary>
	/// Repositions the object using the existing screen-space settings.
	/// </summary>
	void PositionOnScreen()
	{

        // HACK https://cloud.unity.com/home/organizations/33495/projects/fafbc2dd-4fff-4a1e-b611-14f2ed165eef/cloud-diagnostics/crashes-exceptions/problems/6e940ad51008ba1ba149f5a0d31278f3?tag=%21%3DClosed&version.keyword=3.70.1992
        if (renderCamera == null || this == null)
			return;

		Awake ();
		Start ();
		
		framePositioned = Time.frameCount;
		
		// Keep the 'z' updated in the inspector
		if (ignoreZ)
		{
			Plane plane = new Plane(renderCamera.transform.forward, renderCamera.transform.position);
			screenPos.z = plane.GetDistanceToPoint(transform.position);
		}

		if (ignoreZ)
		{
			
			Vector3 pos = ScreenPosToWorldPos(screenPos);
			pos.z = transform.position.z;
			transform.position = pos;
			
		}
		else
		{
			
			transform.position = ScreenPosToWorldPos(screenPos);
			
		}

		// Notify the object that it was repositioned:
		SendMessage("OnReposition", SendMessageOptions.DontRequireReceiver);
		
	}

	/// <summary>
	/// Accessor for the camera that will be used to render this object.
	/// Use this to ensure the object is properly configured for the
	/// specific camera that will render it.
	/// </summary>
	public Camera RenderCamera
	{
		get { return renderCamera; }
		set { SetCamera(value); }
	}

	/// <summary>
	/// Updates the object's position based on the currently
	/// selected renderCamera.
	/// </summary>
	public void UpdateCamera()
	{
		SetCamera(renderCamera);
	}
	
	/// <summary>
	/// A no-argument version of SetCamera() that simply
	/// re-assigns the same camera to the object, forcing
	/// it to recalculate all camera-dependent calculations.
	/// </summary>
	public void SetCamera()
	{
		SetCamera(renderCamera);
	}

	/// <summary>
	/// Sets the camera to be used for calculating positions.
	/// </summary>
	/// <param name="c"></param>
	public void SetCamera(Camera c)
	{
		if (c == null)
			return;

		renderCamera = c;
		
		screenSize.x = renderCamera.pixelWidth;
		screenSize.y = renderCamera.pixelHeight;
		
		if (screenSize.x <= 0) screenSize.x = 1280;
		if (screenSize.y <= 0) screenSize.y = 800;
	
	}


	public void WorldToScreenPos(Vector3 worldPos)
	{
		if (renderCamera == null)
			return;

		Vector3 newPos = renderCamera.WorldToScreenPoint(worldPos);

		switch (relativeTo.horizontal)
		{
			
			case EZScreenPlacement.HORIZONTAL_ALIGN.SCREEN_CENTER:
			
				screenPos.x = newPos.x - (renderCamera.pixelWidth / 2f);
				break;
			
			case EZScreenPlacement.HORIZONTAL_ALIGN.SCREEN_LEFT:
                
                screenPos.x = newPos.x + Screen.width * 0.1f;
				break;
			
			case EZScreenPlacement.HORIZONTAL_ALIGN.SCREEN_RIGHT:
			
				screenPos.x = newPos.x - renderCamera.pixelWidth;
				break;
			
			case EZScreenPlacement.HORIZONTAL_ALIGN.OBJECT:
			
				if (relativeObject != null)
				{
					Vector3 objPos = renderCamera.WorldToScreenPoint(RelativeAnchorPoint);
					screenPos.x = newPos.x - objPos.x;
				}
			
				break;
		}
		
		switch (relativeTo.vertical)
		{
			case EZScreenPlacement.VERTICAL_ALIGN.SCREEN_CENTER:
				screenPos.y = newPos.y - (renderCamera.pixelHeight / 2f);
				break;
			case EZScreenPlacement.VERTICAL_ALIGN.SCREEN_TOP:
				screenPos.y = newPos.y - renderCamera.pixelHeight;
				break;
			case EZScreenPlacement.VERTICAL_ALIGN.SCREEN_BOTTOM:
				screenPos.y = newPos.y;
				break;
			case EZScreenPlacement.VERTICAL_ALIGN.OBJECT:
			
				if (relativeObject != null)
				{
					Vector3 objPos = renderCamera.WorldToScreenPoint(RelativeAnchorPoint);
					screenPos.y = newPos.y - objPos.y;
				}
			
				break;
		}

		screenPos.z = newPos.z;
		
		screenPos.x = (screenPos.x/Screen.width) * 100;
		screenPos.y = (screenPos.y/Screen.height) * 100;
		
		PositionOnScreenRecursively();
		
	}

	/// <summary>
	/// Retrieves the screen coordinate of the object's current position.
	/// </summary>
	public Vector3 ScreenCoord
	{
		get { return renderCamera.WorldToScreenPoint(transform.position); }
	}

	// Tests dependencies for circular dependency.
	// Returns true if safe, false if circular.
	static public bool TestDepenency(EZScreenPlacement sp)
	{
		
		if (sp.relativeObject == null)
			return true;

		// Table of all objects in the chain of dependency:
		List<EZScreenPlacement> objs = new List<EZScreenPlacement>();

		objs.Add(sp);

		EZScreenPlacement curObj = sp.relativeObject.GetComponent(typeof(EZScreenPlacement)) as EZScreenPlacement;

		// Walk the chain:
		while (curObj != null)
		{
			if (objs.Contains(curObj))
				return false; // Circular!

			// Add this one to the list and keep walkin'
			objs.Add(curObj);

			// See if we're at the end of the chain:
			if (curObj.relativeObject == null)
				return true;

			// Get the next one:
			curObj = curObj.relativeObject.GetComponent(typeof(EZScreenPlacement)) as EZScreenPlacement;
		}

		return true;
	}

	public virtual void DoMirror()
	{
		// Only run if we're not playing:
		if (Application.isPlaying)
			return;

		if (mirror == null)
		{
			mirror = new EZScreenPlacementMirror();
			mirror.Mirror(this);
		}

		mirror.Validate(this);

		// Compare our mirrored settings to the current settings
		// to see if something was changed:
		if (mirror.DidChange(this))
		{
			SetCamera(renderCamera);
			mirror.Mirror(this);	// Update the mirror
		}
	}

#if UNITY_EDITOR

	void Update()
	{

		DoMirror();
	}

#endif


		}



// Used to automatically update an EZScreenPlacement object
// when its settings are modified in-editor.
public class EZScreenPlacementMirror
{
	public Vector3 worldPos;
	public Vector3 screenPos;
	public EZScreenPlacement.RelativeTo relativeTo;
	public EZScreenPlacement relativeObject;
	public Camera renderCamera;
	public Vector2 screenSize;

	public EZScreenPlacementMirror()
	{
		relativeTo = new EZScreenPlacement.RelativeTo(null);
	}

	public virtual void Mirror(EZScreenPlacement sp)
	{

		if (renderCamera != null)
		{
				
			worldPos = sp.transform.position;
			screenPos = sp.screenPos;
			relativeTo.Copy(sp.relativeTo);
			relativeObject = sp.relativeObject;
			renderCamera = sp.renderCamera;
			screenSize = new Vector2(sp.renderCamera.pixelWidth, sp.renderCamera.pixelHeight);
			
		}
	}

	public virtual bool Validate(EZScreenPlacement sp)
	{
		// Only allow assignment of a relative object IF
		// we intend to use it:
		if (sp.relativeTo.horizontal != EZScreenPlacement.HORIZONTAL_ALIGN.OBJECT &&
			sp.relativeTo.vertical != EZScreenPlacement.VERTICAL_ALIGN.OBJECT)
			sp.relativeObject = null;

		// See if our dependency is circular:
		if (sp.relativeObject != null)
		{
			if (!EZScreenPlacement.TestDepenency(sp))
			{
				Debug.LogError("ERROR: The Relative Object you recently assigned on \"" + sp.name + "\" which points to \"" + sp.relativeObject.name + "\" would create a circular dependency.  Please check your placement dependencies to resolve this.");
				sp.relativeObject = null;
			}
		}

		return true;
	}

	public virtual bool DidChange(EZScreenPlacement sp)
	{
		if (worldPos != sp.transform.position)
		{
			if (sp.allowTransformDrag)
			{
				// Calculate new screen position:
				sp.WorldToScreenPos(sp.transform.position);
			}
			else
				sp.PositionOnScreenRecursively();
			return true;
		}
		if (screenPos != sp.screenPos)
			return true;
		if (renderCamera != null)
		{
			if (screenSize.x != sp.renderCamera.pixelWidth || screenSize.y != sp.renderCamera.pixelHeight)
			{
				return true;
			}
		}
		if (!relativeTo.Equals(sp.relativeTo))
			return true;
		if (renderCamera != sp.renderCamera)
			return true;
		if (relativeObject != sp.relativeObject)
		{
			return true;
		}

		return false;
	}
}