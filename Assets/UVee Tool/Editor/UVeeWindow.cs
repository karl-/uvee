#pragma warning disable 0649, 0219
#define DEBUG

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

public class UVeeWindow : EditorWindow {

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
	bool drawBoundingBox = false;
	bool drawTriangles = true;
#endregion

#region DATA

	MeshFilter[] selection = new MeshFilter[0];
	int submesh = -1;
	List<int>[] user_selection = new List<int>[0];
	Texture2D tex;
#endregion

#region CONSTANT

	const int MIN_ZOOM = 25;
	const int MAX_ZOOM = 400;

	const int LINE_WIDTH = 1;
	Color LINE_COLOR = Color.gray;

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

	int expandedSettingsHeight = 160;
	int compactSettingsHeight = 50;
	int settingsBoxHeight;

	int settingsBoxPad = 10;
	int settingsMaxWidth = 0;
	Rect settingsBoxRect = new Rect();
	
	Vector2 offset = new Vector2(0f, 0f);
	Vector2 start = new Vector2(0f, 0f);
	bool dragging = false;
#endregion

#region INITIALIZATION
	
	[MenuItem("Window/UVee Window")]
	public static void Init()
	{
		EditorWindow.GetWindow(typeof(UVeeWindow), true, "UVee Viewer", true);
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

			// alt right click and drag == zoom
			if(e.button == 1 && e.modifiers == EventModifiers.Alt)
			{

			}

			// USER INPUT THAT CAN BE DRAWN
			if(e.type == EventType.MouseDown && e.button == 0 && !settingsBoxRect.Contains(e.mousePosition) && e.modifiers != EventModifiers.Alt) {
				drag_start = e.mousePosition;
				mouseDragging = true;
			}

			if(e.type == EventType.MouseUp && e.button == 0) {
				mouseDragging = false;
				UpdateSelectionWithGUIRect(GUIRectWithPoints(drag_start, e.mousePosition));
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
			DrawPoints( UVArrayWithTriangles(selection[i], user_selection[i]), COLOR_ARRAY[i%COLOR_ARRAY.Length]);

		if(drawTriangles)
			DrawTriangles(selection, user_selection);

		if(drawBoundingBox)
			for(int i = 0; i < selection.Length; i++)
				DrawBoundingBox(((uvChannel == UVChannel.UV) ? selection[i].sharedMesh.uv : selection[i].sharedMesh.uv2));

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
					drawTriangles = EditorGUILayout.Toggle("Draw Triangles", drawTriangles, GUILayout.MaxWidth(settingsMaxWidth));

					GUI.changed = false;
					showTex = EditorGUILayout.Toggle("Display Texture", showTex, GUILayout.MaxWidth(settingsMaxWidth));
					if(GUI.changed) OnSelectionChange();

					drawBoundingBox = EditorGUILayout.Toggle("Draw Containing Box", drawBoundingBox, GUILayout.MaxWidth(settingsMaxWidth));
					
					string[] submeshes = new string[selection[0].sharedMesh.subMeshCount+1];
					submeshes[0] = "All";
					for(int i = 1; i < submeshes.Length; i++)
						submeshes[i] = (i-1).ToString();

					submesh = EditorGUILayout.Popup(submesh, submeshes);
				}
				else
					settingsBoxHeight = compactSettingsHeight;
			GUI.EndGroup();
		}

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
		user_selection = new List<int>[selection.Length];

		for(int i = 0; i < selection.Length; i++)
			user_selection[i] = new List<int>(selection[i].sharedMesh.triangles);

		if(selection != null && selection.Length > 0)
			tex = (Texture2D)selection[0].GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
	}
#endregion

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
#if DEBUG
		float start = (float)EditorApplication.timeSinceStartup;
#endif
		foreach(Vector2 uv_coord in points)
		{
			Vector2 guiPoint = UVToGUIPoint(uv_coord);
			GUI.color = col;
				GUI.DrawTexture(new Rect(guiPoint.x-halfDot, guiPoint.y-halfDot, UV_DOT_SIZE, UV_DOT_SIZE), dot, ScaleMode.ScaleToFit);
			GUI.color = Color.white;

			if(showCoordinates)
				GUI.Label(new Rect(guiPoint.x, guiPoint.y, 100, 40), "" + uv_coord);
		}
#if DEBUG
		LogMethodTime("DrawPoints", (float)EditorApplication.timeSinceStartup - start);
#endif
	}

	public void DrawBox(Vector2 p0, Vector2 p1, Color col)
	{
		GUI.backgroundColor = col;
		GUI.Box(GUIRectWithPoints(p0, p1), "");
		GUI.backgroundColor = Color.white;
	}

	public void DrawTriangles(MeshFilter[] mfs, List<int>[] selected)
	{
#if DEBUG
		float start = (float)EditorApplication.timeSinceStartup;
#endif
		Handles.BeginGUI();
		Handles.color = Color.black;

		// doesn't support any different winding types yet
		for(int i = 0; i < mfs.Length; i++)
		{
			Vector2[] uv = (uvChannel == UVChannel.UV) ? mfs[i].sharedMesh.uv : mfs[i].sharedMesh.uv2;
			List<int> tri = selected[i];

			for(int n = 0; n < mfs[i].sharedMesh.triangles.Length; n+=3)
			{
#if DEBUG
				float uvguipointstart = (float)EditorApplication.timeSinceStartup;
#endif
				Vector3 p0 = UVToGUIPoint(uv[mfs[i].sharedMesh.triangles[n+0]]);
				Vector3 p1 = UVToGUIPoint(uv[mfs[i].sharedMesh.triangles[n+1]]);
				Vector3 p2 = UVToGUIPoint(uv[mfs[i].sharedMesh.triangles[n+2]]);
#if DEBUG
				LogMethodTime("UVToGUIPoint Conversion", (float)EditorApplication.timeSinceStartup - uvguipointstart);
#endif

#if DEBUG
				float tristart = (float)EditorApplication.timeSinceStartup;
#endif
				bool p1_s = tri.Contains(mfs[i].sharedMesh.triangles[n+1]);
				bool p0_s = tri.Contains(mfs[i].sharedMesh.triangles[n+0]);
				bool p2_s = tri.Contains(mfs[i].sharedMesh.triangles[n+2]);
#if DEBUG
				LogMethodTime("Tri Contains", (float)EditorApplication.timeSinceStartup - tristart);
#endif

#if DEBUG
				float handlesatrt = (float)EditorApplication.timeSinceStartup;
#endif
				if(p0_s && p1_s) Handles.DrawLine(p0, p1);
				if(p1_s && p2_s) Handles.DrawLine(p1, p2);
				if(p0_s && p2_s) Handles.DrawLine(p2, p0);
#if DEBUG
				LogMethodTime("Draw Triangle Handles", (float)EditorApplication.timeSinceStartup - handlesatrt);
#endif
			}
		}

		Handles.color = Color.white;
		Handles.EndGUI();
#if DEBUG
		LogMethodTime("DrawTriangles", (float)EditorApplication.timeSinceStartup - start);
#endif
	}

	public void DrawBoundingBox(Vector2[] points)
	{
		Vector2 min = Vector2ArrayMin(points);
		Vector2 max = Vector2ArrayMax(points);
		
		GUI.color = new Color(.2f, .2f, .2f, .2f);
			GUI.Box(GUIRectWithPoints( UVToGUIPoint(min), UVToGUIPoint(max)), "");
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

	public Vector2[] UVArrayWithTriangles(MeshFilter mf, List<int> tris)
	{
		List<Vector2> uvs = new List<Vector2>();

		Vector2[] mf_uv = (uvChannel == UVChannel.UV) ? mf.sharedMesh.uv : mf.sharedMesh.uv2;
		for(int n = 0; n < tris.Count; n++)
		{
			// Debug.Log("N INDEX: " + n + "  n value: " + tris[n] + " / " + tris.Count);
			uvs.Add( mf_uv[tris[n]] );
		}

		return uvs.ToArray();
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

	public Vector2 Vector2ArrayMin(Vector2[] val)
	{
		if(val == null || val.Length < 1)
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

	public Vector2 Vector2ArrayMax(Vector2[] val)
	{
		if(val == null || val.Length < 1)
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

#region SCREEN TO UV SPACE CONVERSION AND CHECKS

	public void UpdateSelectionWithGUIRect(Rect rect)
	{
		bool pointSelected = false;
		for(int i = 0; i < selection.Length; i++)
		{
			user_selection[i].Clear();
			Vector2[] uvs = (uvChannel == UVChannel.UV) ? selection[i].sharedMesh.uv : selection[i].sharedMesh.uv2;
			for(int n = 0; n < selection[i].sharedMesh.triangles.Length; n++)
			{
				if(rect.Contains(UVToGUIPoint( uvs[selection[i].sharedMesh.triangles[n]])))
				{
					pointSelected = true;
					user_selection[i].Add( selection[i].sharedMesh.triangles[n] );
				}
			}
		}
		if(!pointSelected)
			OnSelectionChange();
	}

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

#region DEBUG
		
	Dictionary<string, List<float>> methodExecutionTimes = new Dictionary<string, List<float>>();
	public void LogMethodTime(string methodName, float time)
	{
		if(methodExecutionTimes.ContainsKey(methodName))
			methodExecutionTimes[methodName].Add(time);
		else
			methodExecutionTimes.Add(methodName, new List<float>(new float[1]{time}));
	}

	public void OnDisable()
	{
		foreach(KeyValuePair<string, List<float>> kvp in methodExecutionTimes)
		{
			Debug.Log("Method: " + kvp.Key + "\nAvg. Time: " + Average(kvp.Value).ToString("F7"));
		}
	}

	public float Average(List<float> list)
	{
		float avg = 0f;
		for(int i = 0; i < list.Count; i++)
			avg += list[i];
		return avg/(float)list.Count;
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
			c.AddRange(t.GetComponentsInChildren<T>());
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