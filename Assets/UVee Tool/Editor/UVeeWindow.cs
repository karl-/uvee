#pragma warning disable 0649, 0219
#define DEBUG

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
	// bool drawNonSelectedPoints = true;
#endregion

#region DATA

	MeshFilter[] selection = new MeshFilter[0];
	int submesh = 0;
	HashSet<int>[] selected_triangles = new HashSet<int>[0];
	Texture2D tex;

	// GUI draw caches
	Vector2[][] 	uv_points 		= new Vector2[0][];			// all uv points
	Vector2[][] 	user_points 	= new Vector2[0][];
	Vector2[][]		triangle_points = new Vector2[0][];			// wound in twos - so a triangle is { p0, p1, p1, p1, p2, p0 }
	Vector2[][]		user_triangle_points = new Vector2[0][];	// contains only selected triangles
	List<Vector2>	all_points 		= new List<Vector2>();
	Vector2 		uv_center 		= Vector2.zero;

	// selection caches
	int[][]			distinct_triangle_selection = new int[0][];	///< Guarantees that only one index per vertex is present
#endregion

#region CONSTANT

	const int MIN_ZOOM = 25;
	const int MAX_ZOOM = 400;

	const int LINE_WIDTH = 1;
	Color LINE_COLOR = Color.gray;

	const int UV_DOT_SIZE = 4;

	Color[] COLOR_ARRAY = new Color[5];

	Color DRAG_BOX_COLOR = new Color(.6f, .6f, .6f, .45f);
#endregion

#region GUI MEMBERS

	Texture2D dot;
	Texture2D moveTool;
	Rect moveToolRect { get {
			int size = 32;// = moveTool.width / (100/workspace_scale);

			return new Rect(
				uv_center.x - size/2,
				uv_center.y - size/2,
				size,
				size);
		}
	}

	Vector2 center = new Vector2(0f, 0f);			// actual center
	Vector2 workspace_origin = new Vector2(0f, 0f);	// where to start drawing in GUI space
	Vector2 workspace = new Vector2(0f, 0f);		// max allowed size, padding inclusive
	int max = 0;

	int pad = 10;
	int workspace_scale = 100;

	float scale = 0f;

	int expandedSettingsHeight = 180;
	int compactSettingsHeight = 50;
	int settingsBoxHeight;

	int settingsBoxPad = 10;
	int settingsMaxWidth = 0;
	Rect settingsBoxRect = new Rect();
	
	Vector2 offset = new Vector2(0f, 0f);
	Vector2 start = new Vector2(0f, 0f);
	bool dragging = false;

	bool scrolling = false;
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
		moveTool = (Texture2D)Resources.Load("move", typeof(Texture2D));
		PopulateColorArray();
		OnSelectionChange();
		Repaint();
	}
#endregion

#region GUI
	
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

		if(e.isMouse && !settingsBoxRect.Contains(e.mousePosition) && !moveToolRect.Contains(e.mousePosition))
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
			if(e.type == EventType.MouseDown && e.button == 0 && e.modifiers != EventModifiers.Alt) {
				drag_start = e.mousePosition;
				mouseDragging = true;
			}
		}

		// dragging uvs around
		if(e.isMouse && e.button == 0 && e.modifiers == (EventModifiers)0 && !settingsBoxRect.Contains(e.mousePosition) && moveToolRect.Contains(e.mousePosition))
			UVMoveTool();

		if(e.type == EventType.MouseUp && e.button == 0 && mouseDragging) {
			mouseDragging = false;
			UpdateSelectionWithGUIRect(GUIRectWithPoints(drag_start, e.mousePosition), e.modifiers == EventModifiers.Shift);
		}

		// Scale
		if(e.type == EventType.ScrollWheel)
		{
			float modifier = -1f;
			workspace_scale = (int)Mathf.Clamp(workspace_scale + (e.delta.y * modifier), MIN_ZOOM, MAX_ZOOM);
			Repaint();
			scrolling = true;
		}

		if(e.isKey && e.keyCode == KeyCode.Alpha0) {
			offset = Vector2.zero;
			workspace_scale = 100;
			Repaint();
			UpdateGUIPointCache();
		}

		if(e.type == EventType.MouseUp)
		{
			UpdateGUIPointCache();
			Repaint();
		}

		DrawGraphBase();

		if(drawTriangles)
			for(int i = 0; i < selected_triangles.Length; i++)
				DrawLines(user_triangle_points[i], COLOR_ARRAY[i%COLOR_ARRAY.Length]);

		if(drawTriangles)
			for(int i = 0; i < selected_triangles.Length; i++)
				DrawLines(triangle_points[i], new Color(.2f, .2f, .2f, .2f));

		if(drawBoundingBox)
			for(int i = 0; i < selection.Length; i++)
				DrawBoundingBox(user_points[i]);

		for(int i = 0; i < selection.Length; i++)
			DrawPoints( user_points[i] );//, COLOR_ARRAY[i%COLOR_ARRAY.Length]);


		//** Draw Preferences Pane **//
		DrawPreferencesPane();

		// move tools	
		// GUI.DrawTexture(new Rect(uv_center.x - moveTool.width/2, uv_center.y - moveTool.height/2, moveTool.width, moveTool.height), moveTool);
		GUI.DrawTexture(moveToolRect, moveTool, ScaleMode.ScaleToFit, true, 0);

		if(mouseDragging) {
			if(Vector2.Distance(drag_start, e.mousePosition) > 10)
				DrawBox(drag_start, e.mousePosition, DRAG_BOX_COLOR);
			Repaint();
		}

		if(dragging)	
			UpdateGUIPointCache();

		if(scrolling) {
			scrolling = false;
			UpdateGUIPointCache();
			Repaint();
		}
	}
#endregion

#region EVENT

	public void OnFocus()
	{
		OnSelectionChange();
	}

	public void OnSelectionChange()
	{
		selection = TransformExtensions.GetComponents<MeshFilter>(Selection.transforms);
		selected_triangles = new HashSet<int>[selection.Length];

		for(int i = 0; i < selection.Length; i++)
			selected_triangles[i] = new HashSet<int>();

		if(selection != null && selection.Length > 0)
		{
			// ??
			Object t = selection[0].GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
			tex = (t == null) ? null : (Texture2D)t;
		}
		
		UpdateGUIPointCache();
	}

	public void UpdateSelectionWithGUIRect(Rect rect, bool shift)
	{
		bool pointSelected = false;
		// avoid if checks if shift isn't held - (this loop is already slow, so take speed improvements where we can)
		if(!shift)
		{
			for(int i = 0; i < selection.Length; i++)
			{
				selected_triangles[i].Clear();
				int[] tris = selection[i].sharedMesh.triangles;
				for(int n = 0; n < tris.Length; n++)
				{
					if(rect.Contains(uv_points[i][tris[n]]))
					{
						pointSelected = true;
						selected_triangles[i].Add( tris[n] );
					}
				}
			}
		}
		else
		{
			for(int i = 0; i < selection.Length; i++)
			{
				selected_triangles[i].Clear();
				int[] tris = selection[i].sharedMesh.triangles;
				for(int n = 0; n < tris.Length; n++)
				{
					if(rect.Contains(uv_points[i][tris[n]]))
					{
						pointSelected = true;
						selected_triangles[i].Add( tris[n] );
					}
				}
			}
		}

		if(!pointSelected)
			OnSelectionChange();

		UpdateGUIPointCache();
	}

	// Call after UVs are selected, or the GUI space has been modified
	public void UpdateGUIPointCache()
	{	
		LogStart("UpdateGUIPointCache");

		uv_points 					= new Vector2[selection.Length][];
		user_points 				= new Vector2[selection.Length][];
		triangle_points 			= new Vector2[selection.Length][];
		user_triangle_points 		= new Vector2[selection.Length][];
		distinct_triangle_selection = new int 	 [selection.Length][];

		all_points = new List<Vector2>();

		for(int i = 0; i < selection.Length; i++)
		{
			uv_points[i] = UVToGUIPoint((uvChannel == UVChannel.UV) ? selection[i].sharedMesh.uv : selection[i].sharedMesh.uv2);
			user_points[i] = UVToGUIPoint(UVArrayWithTriangles(selection[i], selected_triangles[i]));
			all_points.AddRange(user_points[i]);

			int[] tris = selection[i].sharedMesh.triangles;
			List<Vector2> lines = new List<Vector2>();
			List<Vector2> u_lines = new List<Vector2>();
			for(int n = 0; n < tris.Length; n+=3)
			{
				Vector2 p0 = uv_points[i][tris[n+0]];
				Vector2 p1 = uv_points[i][tris[n+1]];
				Vector2 p2 = uv_points[i][tris[n+2]];

				// this could be sped up
				bool p0_s = selected_triangles[i].Contains(tris[n+0]);
				bool p1_s = selected_triangles[i].Contains(tris[n+1]);
				bool p2_s = selected_triangles[i].Contains(tris[n+2]);
				if(p0_s && p1_s) { u_lines.Add(p0); u_lines.Add(p1); }
				if(p1_s && p2_s) { u_lines.Add(p1); u_lines.Add(p2); }
				if(p0_s && p2_s) { u_lines.Add(p2); u_lines.Add(p0); }

				lines.AddRange(new Vector2[6] {p0, p1, p1, p2, p2, p0});
			}

			triangle_points[i] = lines.ToArray();
			user_triangle_points[i] = u_lines.ToArray();
			distinct_triangle_selection[i] = selected_triangles[i].Distinct().ToArray();
		}

		uv_center = Average(all_points);

		LogFinish("UpdateGUIPointCache");
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

		foreach(Vector2 guiPoint in points)
		{
			// Vector2 guiPoint = UVToGUIPoint(uv_coord);
			GUI.color = col;
				GUI.DrawTexture(new Rect(guiPoint.x-halfDot, guiPoint.y-halfDot, UV_DOT_SIZE, UV_DOT_SIZE), dot, ScaleMode.ScaleToFit);
			GUI.color = Color.white;

			// if(showCoordinates)
			// 	GUI.Label(new Rect(guiPoint.x, guiPoint.y, 100, 40), "" + uv_coord);
		}
	}

	public void DrawBox(Vector2 p0, Vector2 p1, Color col)
	{
		GUI.backgroundColor = col;
		GUI.Box(GUIRectWithPoints(p0, p1), "");
		GUI.backgroundColor = Color.white;
	}

	public void DrawLines(Vector2[] points, Color col)
	{
		Handles.BeginGUI();
		Handles.color = col;

			for(int i = 0; i < points.Length; i+=2)
				Handles.DrawLine(
					points[i+0],
					points[i+1]);

		Handles.color = Color.white;
		Handles.EndGUI();
	}

	public void DrawBoundingBox(Vector2[] points)
	{
		Vector2 min = Vector2ArrayMin(points);
		Vector2 max = Vector2ArrayMax(points);
		
		GUI.color = new Color(.2f, .2f, .2f, .2f);
			GUI.Box(GUIRectWithPoints( min, max), "");
		GUI.color = Color.white;
	}

	public void DrawPreferencesPane()
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
				
				string[] submeshes = new string[ (selection != null && selection.Length > 0) ? selection[0].sharedMesh.subMeshCount+1 : 1];
				submeshes[0] = "All";
				for(int i = 1; i < submeshes.Length; i++)
					submeshes[i] = (i-1).ToString();

				submesh = EditorGUILayout.Popup("Submesh", submesh, submeshes);
			}
			else
				settingsBoxHeight = compactSettingsHeight;
		GUI.EndGroup();
	}
#endregion

#region TOOLS
	
	bool dragging_uv = false;
	Vector2 dragging_uv_start = Vector2.zero;
	public void UVMoveTool()
	{
		Event e = Event.current;
		if(e.type == EventType.MouseDown) 
		{
			dragging_uv = true;
			dragging_uv_start = e.mousePosition;
			Undo.SetSnapshotTarget(TransformExtensions.GetMeshes(Selection.transforms) as Object[], "Move UVs");
			// Undo.RegisterSceneUndo("Move UVs");
			for(int i = 0; i < Selection.transforms.Length; i++)
				EditorUtility.SetDirty(Selection.transforms[i]);
			Undo.CreateSnapshot();
			Undo.RegisterSnapshot();
		}

		if(dragging_uv)
		{
			Vector2 delta = GUIToUVPoint(dragging_uv_start) - GUIToUVPoint(e.mousePosition);
			dragging_uv_start = e.mousePosition;
			TranslateUVs(distinct_triangle_selection, delta);

			Repaint();
			UpdateGUIPointCache();
		}

		if(e.type == EventType.MouseUp || e.type == EventType.Ignore)
		{
			dragging_uv = false;
			UpdateGUIPointCache();
		}
	}
#endregion

#region UV UTILITY

	public void TranslateUVs(int[][] uv_selection, Vector2 uvDelta)
	{
		for(int i = 0; i < selection.Length; i++)
		{
			Vector2[] uvs = (uvChannel == UVChannel.UV) ? selection[i].sharedMesh.uv : selection[i].sharedMesh.uv2;
			for(int n = 0; n < uv_selection[i].Length; n++)
			{
				uvs[uv_selection[i][n]] -= uvDelta;
			}

			if(uvChannel == UVChannel.UV)
				selection[i].sharedMesh.uv = uvs;
			else
				selection[i].sharedMesh.uv2 = uvs;
		}
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

	public Vector2[] UVArrayWithTriangles(MeshFilter mf, HashSet<int> tris)
	{
		List<Vector2> uvs = new List<Vector2>();

		Vector2[] mf_uv = (uvChannel == UVChannel.UV) ? mf.sharedMesh.uv : mf.sharedMesh.uv2;

		foreach(int tri in tris)
			uvs.Add(mf_uv[tri]);
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
	
	public Vector2[] UVToGUIPoint(Vector2[] uvs)
	{
		Vector2[] uv = new Vector2[uvs.Length];
		for(int i = 0; i < uv.Length; i++)
			uv[i] = UVToGUIPoint(uvs[i]);
		return uv;
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

	public Vector2 GUIToUVPoint(Vector2 gui)
	{
		gui -= center;
		gui /= scale;
		Vector2 u = new Vector2(gui.x, -gui.y);

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

	Dictionary<string, float> timer = new Dictionary<string, float>();
	public void LogStart(string methodName)
	{
		if(methodExecutionTimes.ContainsKey(methodName))
			timer[methodName] = (float)EditorApplication.timeSinceStartup;
		else
			timer.Add(methodName, (float)EditorApplication.timeSinceStartup);
	}

	public void LogFinish(string methodName)
	{
		LogMethodTime(methodName, (float)EditorApplication.timeSinceStartup - timer[methodName]);
	}

	public void OnDisable()
	{
		foreach(KeyValuePair<string, List<float>> kvp in methodExecutionTimes)
		{
			Debug.Log("Method: " + kvp.Key + "\nAvg. Time: " + Average(kvp.Value).ToString("F7") + "\nSamples: " + kvp.Value.Count);
		}
	}

	public float Average(List<float> list)
	{
		float avg = 0f;
		for(int i = 0; i < list.Count; i++)
			avg += list[i];
		return avg/(float)list.Count;
	}

	public Vector2 Average(List<Vector2> list)
	{
		Vector2 avg = Vector2.zero;
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

	public static Mesh[] GetMeshes(Transform[] t_arr)
	{
		MeshFilter[] mfs = GetComponents<MeshFilter>(t_arr);
		Mesh[] m = new Mesh[mfs.Length];
		for(int i = 0; i < mfs.Length; i++)
			m[i] = mfs[i].sharedMesh;
		return m;
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