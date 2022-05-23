using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(UV_Test))]

public class UV_Test_Editor : Editor
{
    public override void OnInspectorGUI()
    {
        UV_Test tester = (UV_Test)target;
        if(GUILayout.Button("Build UV"))
        {
            tester.createUVs();
        }
    }
}
