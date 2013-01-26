#pragma warning disable 0649, 0219

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

public class UV_Viewer : EditorWindow {

#region ENUM

	public enum UVChannel {
		UV,
		UV2
	}
#endregion

#region SETTINGS

	bool showPreferences = true;
	UVChannel uvChannel = UVChannel.UV;
	bool showCoordinates = false;
	bool showTex = true;
	bool drawBoundingBox = true;
	bool drawTriangles = true;
#endregion

#region DATA

	MeshFilter[] selection = new MeshFilter[0];

	Texture2D tex;
#endregion

#region CONSTANT

	const int MIN_ZOOM = 25;
	const int MAX_ZOOM = 400;

	const int LINE_WIDTH = 1;

	const int UV_DOT_SIZE = 4;

	Color[] COLOR_ARRAY = new Color[5];

	Color DRAG_BOX_COLOR = new Color(0f, 0f, .6f, .3f);
#endregion

#region GUI MEMBERS

	Texture2D dot;
	
	Vector2 center = new Vector2(0f, 0f);			// actual center
	Vector2 workspace_origin = new Vector2(0f, 0f);	// where to start drawing in GUI space
	Vector2 workspace = new Vector2(0f, 0f);		// max allowed size, padding inclusive
	int max = 0;

	int pad = 10;
	int workspace_scale = 100;

	float scale = 0f;

	Color LINE_COLOR = Color.blue;

	int expandedSettingsHeight = 136;
	int compactSettingsHeight = 50;
	int settingsBoxHeight = 130;

	int settingsBoxPad = 10;
	int settingsMaxWidth = 0;
	Rect settingsBoxRect = new Rect();
	
	Vector2 offset = new Vector2(0f, 0f);
	Vector2 start = new Vector2(0f, 0f);
	bool dragging = false;
#endregion

#region INITIALIZATION
	
	[MenuItem("Window/UV Viewer")]
	public static void Init()
	{
		EditorWindow.GetWindow(typeof(UV_Viewer), true, "UV Viewer", true);
	}

	public void OnEnable()
	{	
		dot = (Texture2D)Resources.Load("dot", typeof(Texture2D));
		PopulateColorArray();
		OnSelectionChange();
	}
#endregion

#region UPDATE

	// force update window
	void OnInspectorUpdate()
	{
		if(EditorWindow.focusedWindow != this)
	    	Repaint();
	}

	Vector2 drag_start; 
	bool mouseDragging = false;
	void OnGUI()
	{
		//** Handle events **//

		Event e = Event.current;
		if(e.isMouse)
		{
			if(e.button == 2 || (e.modifiers == EventModifiers.Alt && e.button == 0))
			{
				if(e.type == EventType.MouseDown) {
					start = e.mousePosition;
					dragging = true;
				}

				if(dragging) {
					offset = offset + (e.mousePosition - start);
					start = e.mousePosition;
					Repaint();
				}

				if(e.type == EventType.MouseUp || e.type == EventType.Ignore) {
					dragging = false;
				}
			}

			// USER INPUT THAT CAN BE DRAWN
			if(e.type == EventType.MouseDown && e.button == 0) {
				drag_start = e.mousePosition;
				mouseDragging = true;
			}

			if(e.type == EventType.MouseUp && e.button == 0) {
				mouseDragging = false;
			}

		}

		// SCale
		if(e.type == EventType.ScrollWheel)
		{
			float modifier = -1f;
			workspace_scale = (int)Mathf.Clamp(workspace_scale + (e.delta.y * modifier), MIN_ZOOM, MAX_ZOOM);
			Repaint();
		}

		if(e.isKey && e.keyCode == KeyCode.Alpha0) {
			offset = Vector2.zero;
			workspace_scale = 100;
			Repaint();
		}


		DrawGraphBase();

		for(int i = 0; i < selection.Length; i++)
			DrawPoints(selection[i].sharedMesh.uv, COLOR_ARRAY[i%COLOR_ARRAY.Length]);

		if(drawTriangles)
			DrawTriangles(selection);

		//** Draw Preferences Pane **//
		{
			settingsBoxRect = new Rect(settingsBoxPad, settingsBoxPad, Screen.width-settingsBoxPad*2, settingsBoxHeight-settingsBoxPad);
			settingsMaxWidth = (int)settingsBoxRect.width-settingsBoxPad*2;

			GUI.Box(settingsBoxRect, "");
			GUI.BeginGroup(settingsBoxRect);

			GUILayout.BeginHorizontal();
				showPreferences = EditorGUILayout.Foldout(showPreferences, "Preferences");
			GUILayout.EndHorizontal();
				if(showPreferences)
				{
					settingsBoxHeight = expandedSettingsHeight;
					workspace_scale = EditorGUILayout.IntSlider("Scale", workspace_scale, MIN_ZOOM, MAX_ZOOM, GUILayout.MaxWidth(settingsMaxWidth));
					uvChannel = (UVChannel)EditorGUILayout.EnumPopup("UV Channel", uvChannel, GUILayout.MaxWidth(settingsMaxWidth));
					
					showCoordinates = EditorGUILayout.Toggle("Display Coordinates", showCoordinates, GUILayout.MaxWidth(settingsMaxWidth));
					
					GUI.changed = false;
					showTex = EditorGUILayout.Toggle("Display Texture", showTex, GUILayout.MaxWidth(settingsMaxWidth));
					if(GUI.changed) OnSelectionChange();

					drawBoundingBox = EditorGUILayout.Toggle("Draw Containing Box", drawBoundingBox, GUILayout.MaxWidth(settingsMaxWidth));
				}
				else
					settingsBoxHeight = compactSettingsHeight;
			GUI.EndGroup();
		}

		Handles.BeginGUI();
		Handles.color = Color.black;
			Handles.PositionHandle(new Vector3(e.mousePosition.x, e.mousePosition.y, 0f), Quaternion.identity);
		Handles.color = Color.white;
		Handles.EndGUI();

		if(mouseDragging) {
			DrawBox(drag_start, e.mousePosition, DRAG_BOX_COLOR);
			Repaint();
		}
	}
#endregion

#region EVENT

	public void OnSelectionChange()
	{
		selection = TransformExtensions.GetComponents<MeshFilter>(Selection.transforms);
	}
#endregion
/**
 *	All drawing methods assume coordinates are already in GUI Space
 */
#region DRAWING

	public void DrawGraphBase()
	{
		max = (Screen.width > Screen.height-settingsBoxHeight) ? (Screen.height-settingsBoxHeight) - (pad*2) : Screen.width - (pad*2);
		workspace = new Vector2(max, max);
		workspace *= (workspace_scale/100f);

		scale = workspace.x/2f;

		center = new Vector2(Screen.width / 2, (Screen.height + settingsBoxHeight) / 2 );

		center += offset;

		workspace_origin = new Vector2(center.x-workspace.x/2, center.y-workspace.y/2);

		// Draw the background gray workspace
		GUI.Box(new Rect(workspace_origin.x, workspace_origin.y, workspace.x, workspace.y), "");

		// Draw texture (if it exists)
		if(showTex && tex)
			GUI.DrawTexture(new Rect(center.x, center.y-workspace.y/2, workspace.x/2, workspace.y/2), tex, ScaleMode.ScaleToFit);

		GUI.color = LINE_COLOR;
		// Draw vertical line
		GUI.DrawTexture(new Rect( center.x, workspace_origin.y, LINE_WIDTH, workspace.y), dot);

		// Draw horizontal line
		GUI.DrawTexture(new Rect(workspace_origin.x, center.y, workspace.x, LINE_WIDTH), dot);
		GUI.color = Color.white;
	}
	
	int halfDot = 1;
	public void DrawPoints(Vector2[] points)
	{
		DrawPoints(points, Color.blue);
	}

	public void DrawPoints(Vector2[] points, Color col)
	{
		halfDot = UV_DOT_SIZE / 2;

		foreach(Vector2 uv_coord in points)
		{
			Vector2 guiPoint = UVToGUIPoint(uv_coord);
			GUI.color = col;
				GUI.DrawTexture(new Rect(guiPoint.x-halfDot, guiPoint.y-halfDot, UV_DOT_SIZE, UV_DOT_SIZE), dot, ScaleMode.ScaleToFit);
			GUI.color = Color.white;

			if(showCoordinates)
				GUI.Label(new Rect(guiPoint.x, guiPoint.y, 100, 40), "" + uv_coord);
		}
	}

	public void DrawBox(Vector2 p0, Vector2 p1, Color col)
	{
		GUI.backgroundColor = col;
		GUI.Box(GUIRectWithPoints(p0, p1), "");
		GUI.backgroundColor = Color.white;
	}

	public void DrawTriangles(MeshFilter[] mfs)
	{
		// Handles.BeginGUI();
		// Handles.color = Color.black;

		// foreach(MeshFilter mf in mfs)
		// {
		// 	Vector2[] uv = mf.sharedMesh.uv;
		// 	int[] tri = mf.sharedMesh.triangles;
		// 	for(int i = 0; i < mf.sharedMesh.triangles.Length-1; i++)
		// 	{
		// 		Vector3 p0 = UVToGUIPoint(uv[tri[i]]);
		// 		Vector3 p1 = UVToGUIPoint(uv[tri[i+1]]);

		// 		Handles.DrawLine(p0, p1);
		// 	}
		// }

		// Handles.color = Color.white;
		// Handles.EndGUI();
	}

	public void DrawBoundingBox(List<Vector2> points)
	{
		Vector2 min = Vector2ArrayMin(points);
		Vector2 max = Vector2ArrayMax(points);
		
		GUI.color = new Color(.2f, .2f, .2f, .2f);
			GUI.Box( new Rect( min.x, min.y, max.x - min.x, max.y-min.y), "");
		GUI.color = Color.white;
	}
#endregion

#region UTILITY

	public Color RandomColor()
	{
		return new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), 1f);
	}

	public void PopulateColorArray()
	{
		// for(int i = 0; i < COLOR_ARRAY.Length; i++)
		// 	COLOR_ARRAY[i] = RandomColor();

		COLOR_ARRAY[0] = Color.green;
		COLOR_ARRAY[1] = Color.cyan;
		COLOR_ARRAY[2] = Color.blue;
		COLOR_ARRAY[3] = Color.black;
		COLOR_ARRAY[4] = Color.magenta;
	}

	// Returns a rect in GUI coordinates
	public Rect GUIRectWithPoints(Vector2 p0, Vector2 p1)
	{
		float minX = p0.x < p1.x ? p0.x : p1.x;
		float minY = p0.y < p1.y ? p0.y : p1.y;

		float maxX = p0.x > p1.x ? p0.x : p1.x;
		float maxY = p0.y > p1.y ? p0.y : p1.y;

		return new Rect(minX, minY, maxX - minX, maxY - minY);
	}

	public Vector2 Vector2ArrayMin(List<Vector2> val)
	{
		if(val == null || val.Count < 1)
			return Vector2.zero;

		float x = val[0].x, y = val[0].y;
		
		foreach(Vector2 v in val)
		{
			if(v.x < x)
				x = v.x;
			if(v.y < y)
				y = v.y;
		}
		return new Vector2(x, y);
	}

	public Vector2 Vector2ArrayMax(List<Vector2> val)
	{
		if(val == null || val.Count < 1)
			return Vector2.zero;

		float x = val[0].x, y = val[0].y;
		
		foreach(Vector2 v in val)
		{
			if(v.x > x)
				x = v.x;
			if(v.y > y)
				y = v.y;
		}
		return new Vector2(x, y);
	}
#endregion

#region CONVERSION

	public Vector2 UVToGUIPoint(Vector2 uv)
	{
		// flip y
		Vector2 u = new Vector2(uv.x, -uv.y);

		// offset
		u *= scale;
		u += center;
		// u -= new Vector2(buttonSize/2f, buttonSize/2f);
		u = new Vector2(Mathf.Round(u.x), Mathf.Round(u.y));
		return u;
	}
#endregion
}

public static class TransformExtensions
{
	public static T[] GetComponents<T>(Transform[] t_arr) where T : Component
	{
		List<T> c = new List<T>();
		foreach(Transform t in t_arr)
		{
			if(t.GetComponent<T>())	
				c.Add(t.GetComponent<T>());
		}
		return c.ToArray() as T[];
	}

	public static GameObject[] GetGameObjectsWithComponent<T>(Transform[] t_arr) where T : Component
	{
		List<GameObject> c = new List<GameObject>();
		foreach(Transform t in t_arr)
		{
			if(t.GetComponent<T>())	
				c.Add(t.gameObject);
		}
		return c.ToArray() as GameObject[];
	}
}