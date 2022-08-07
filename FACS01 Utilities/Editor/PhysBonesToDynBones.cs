#if UNITY_EDITOR && (VRC_SDK_VRCSDK2 || VRC_SDK_VRCSDK3)
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace FACS01.Utilities
{
    public class PhysBonesToDynBones : EditorWindow
    {
        private static FACSGUIStyles FacsGUIStyles;
        private static EditorWindow window;
        private static GameObject ToConvert;
        private static bool makeDuplicate;
        private static string output_print;
        
        private const string DynBoneQN = "DynamicBone, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        private const string DynBoneColliderQN = "DynamicBoneCollider, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        private const string DynBonePlaneColliderQN = "DynamicBonePlaneCollider, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
        private static readonly Type DynBoneT = Type.GetType(DynBoneQN);
        private static readonly Type DynBoneColliderT = Type.GetType(DynBoneColliderQN);
        private static readonly Type DynBonePlaneColliderT = Type.GetType(DynBonePlaneColliderQN);
        private static readonly bool hasDynBones = DynBoneT != null && DynBoneColliderT != null && DynBonePlaneColliderT != null;

        private static AnimationCurve MaxAngleToStiff;
        private static VRCPhysBoneCollider[] pbcList;
        private static List<MonoBehaviour> dbcList;

        [MenuItem("FACS Utils/Misc/PhysBones to Dynamic Bones", false, 1000)]
        public static void ShowWindow()
        {
            SelectionChange(); ToConvert = null;
            window = GetWindow(typeof(PhysBonesToDynBones), false, "PhysBones To DynBones", true);
            window.maxSize = new Vector2(500, 400);
        }

        private static void SelectionChange()
        {
            makeDuplicate = true;
            output_print = "";
        }

        public void OnGUI()
        {
            if (FacsGUIStyles == null) { FacsGUIStyles = new FACSGUIStyles(); FacsGUIStyles.helpbox.alignment = TextAnchor.MiddleCenter; }

            EditorGUILayout.LabelField($"<color=cyan><b>PhysBones to Dynamic Bones</b></color>\nScans the selected GameObject and converts all VRCPhysbone bones and colliders (sphere, capsule and plane) to Dynamic Bones.", FacsGUIStyles.helpbox);

            if (hasDynBones)
            {
                EditorGUI.BeginChangeCheck();
                ToConvert = (GameObject)EditorGUILayout.ObjectField(ToConvert, typeof(GameObject), true, GUILayout.Height(40));
                if (EditorGUI.EndChangeCheck()) SelectionChange();

                if (ToConvert)
                {
                    makeDuplicate = GUILayout.Toggle(makeDuplicate, "Make Duplicate? (don't edit original)", GUILayout.Height(30));
                    if (GUILayout.Button("Convert!", FacsGUIStyles.button, GUILayout.Height(40))) Conversion();
                }

                if (!String.IsNullOrEmpty(output_print)) EditorGUILayout.LabelField(output_print, FacsGUIStyles.helpbox);
            }
            else EditorGUILayout.LabelField("You don't have Dynamic Bone installed in this project!", FacsGUIStyles.helpbox);
        }

        private void Conversion()
        {
            output_print = ""; InitConversionTables();

            pbcList = ToConvert.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            var pbList = ToConvert.GetComponentsInChildren<VRCPhysBone>(true);
            if (pbcList.Length == 0 && pbList.Length == 0)
            {
                output_print = "There is no VRCPhysBone or VRCPhysBoneCollider in this GameObject!";
                return;
            }
            if (makeDuplicate)
            {
                MakeDuplicate();
                pbcList = ToConvert.GetComponentsInChildren<VRCPhysBoneCollider>(true);
                pbList = ToConvert.GetComponentsInChildren<VRCPhysBone>(true);
            }
            dbcList = new List<MonoBehaviour>();
            foreach (var col in pbcList)
            {
                if (col.shapeType == VRCPhysBoneColliderBase.ShapeType.Plane) AddDBPlaneCol(dbcList, col);
                else AddDBCol(dbcList, col);
            }
            foreach (var bone in pbList) AddDB(bone);

            foreach (var pbc in pbcList) Component.DestroyImmediate(pbc);
            foreach (var pb in pbList) Component.DestroyImmediate(pb);

            output_print = $" - VRCPhysBones converted: {pbList.Length}\n" +
                $" - VRCPhysBoneCollider converted: {dbcList.Count}";
        }

        private void AddDB(VRCPhysBone physbone)
        {
            physbone.InitTransforms(false);
            var newBone = (MonoBehaviour)physbone.gameObject.AddComponent(DynBoneT);
            //lossless
            newBone.enabled = physbone.enabled;
            DynBoneT.GetField("m_Root").SetValue(newBone, physbone.rootTransform);
            DynBoneT.GetField("m_Exclusions").SetValue(newBone, physbone.ignoreTransforms);
            DynBoneT.GetField("m_Elasticity").SetValue(newBone, physbone.pull);
            DynBoneT.GetField("m_ElasticityDistrib").SetValue(newBone, physbone.pullCurve);
            DynBoneT.GetField("m_Inert").SetValue(newBone, physbone.immobile);
            DynBoneT.GetField("m_InertDistrib").SetValue(newBone, physbone.immobileCurve);

            DynBoneT.GetField("m_Radius").SetValue(newBone, physbone.radius*Mathf.Abs(physbone.rootTransform.lossyScale.x)/Mathf.Abs(physbone.gameObject.transform.lossyScale.x));
            DynBoneT.GetField("m_RadiusDistrib").SetValue(newBone, physbone.radiusCurve);

            //lossy
            int fa = 0;
            if (physbone.limitType == VRCPhysBoneBase.LimitType.Hinge)
            {
                if (physbone.staticFreezeAxis == Vector3.right) fa = 1;
                else if (physbone.staticFreezeAxis == Vector3.up) fa = 2;
                else if (physbone.staticFreezeAxis == Vector3.forward) fa = 3;
            }
            DynBoneT.GetField("m_FreezeAxis").SetValue(newBone, fa);

            float f2 = Mathf.Max(1E-05f, AverageWorldBoneLength(physbone));
            float f = -physbone.gravity * f2 / Mathf.Abs(physbone.gameObject.transform.lossyScale.x);
            if (physbone.gravityFalloff == 1f)
            {
                DynBoneT.GetField("m_Gravity").SetValue(newBone, new Vector3(0f, f, 0f));
            }
            else if (physbone.gravityFalloff == 0f)
            {
                DynBoneT.GetField("m_Force").SetValue(newBone, new Vector3(0f, f, 0f));
            }
            else
            {
                float f3 = Mathf.Sin(2f * Mathf.PI * physbone.gravityFalloff);
                DynBoneT.GetField("m_Gravity").SetValue(newBone, new Vector3(0f, f*f3, 0f));
                DynBoneT.GetField("m_Force").SetValue(newBone, new Vector3(0f, f*(1-f3), 0f));
            }

            float damping; AnimationCurve dampingDistrib;
            if (physbone.springCurve == null || physbone.springCurve.length == 0 || IsConstantCurve(physbone.springCurve) == (true, 1))
            {
                damping = 1 - physbone.spring; dampingDistrib = null;
            }
            else
            {
                damping = Math.Min(1, CurveAbsMaxValue(physbone.springCurve, 1, -1));
                Keyframe[] kf = new Keyframe[physbone.springCurve.length];
                for (int i = 0; i < physbone.springCurve.length; i++)
                {
                    float t = physbone.springCurve.keys[i].time;
                    float v = (1 - physbone.springCurve.keys[i].value)/ damping;
                    kf[i] = new Keyframe(t, v);
                }
                dampingDistrib = new AnimationCurve(kf);
                for (int i = 0; i < dampingDistrib.length; i++)
                {
                    dampingDistrib.SmoothTangents(i, 0f);
                }
            }
            DynBoneT.GetField("m_Damping").SetValue(newBone, damping);
            DynBoneT.GetField("m_DampingDistrib").SetValue(newBone, dampingDistrib);

            float stiffness; AnimationCurve stiffnessDistrib;
            if (physbone.maxAngleXCurve == null || physbone.maxAngleXCurve.length == 0 || IsConstantCurve(physbone.maxAngleXCurve) == (true, 1))
            {
                stiffness = MaxAngleToStiff.Evaluate(physbone.maxAngleX);
                stiffnessDistrib = null;
            }
            else
            {
                var ts = TrueStiffnessCurve(physbone.maxAngleXCurve);
                stiffness = Math.Min(1, CurveAbsMaxValue(ts, 0, 1));
                Keyframe[] kfs = new Keyframe[physbone.maxAngleXCurve.length];
                for (int i = 0; i < physbone.maxAngleXCurve.length; i++)
                {
                    float t = physbone.maxAngleXCurve.keys[i].time;
                    float v = ts.keys[i].value / stiffness;
                    kfs[i] = new Keyframe(t, v);
                }
                stiffnessDistrib = new AnimationCurve(kfs);
                for (int i = 0; i < stiffnessDistrib.length; i++)
                {
                    stiffnessDistrib.SmoothTangents(i, 0);
                }
            }
            DynBoneT.GetField("m_Stiffness").SetValue(newBone, stiffness);
            DynBoneT.GetField("m_StiffnessDistrib").SetValue(newBone, stiffnessDistrib);

            if (physbone.colliders != null && physbone.colliders.Count > 0)
            {
                List<MonoBehaviour> cols = new List<MonoBehaviour>();
                foreach (var col in physbone.colliders)
                {
                    int ind = Array.IndexOf(pbcList, col);
                    if (ind >= 0 && dbcList[ind] != null)
                    {
                        cols.Add(dbcList[ind]);
                    }
                }
                DynBoneT.GetField("m_Colliders").SetValue(newBone, cols);
            }
        }

        private AnimationCurve TrueStiffnessCurve(AnimationCurve ac)
        {
            Keyframe[] kfs = new Keyframe[ac.length];
            for (int i = 0; i < kfs.Length; i++)
            {
                float num = i / (float)(kfs.Length - 1);
                float num2 = MaxAngleToStiff.Evaluate(180f * ac.keys[i].value);
                kfs[i] = new Keyframe(num, num2);
            }
            AnimationCurve nac = new AnimationCurve(kfs);
            for (int i = 0; i < nac.length; i++)
            {
                nac.SmoothTangents(i, 0f);
            }
            return nac;
        }

        private (bool,float) IsConstantCurve(AnimationCurve ac)
        {
            var val1 = ac.keys[0].value;
            for (int i = 1; i < ac.keys.Length; i++)
            {
                if (val1 != ac.keys[i].value) return (false,-1);
            }
            return (true, val1);
        }

        private float CurveAbsMaxValue(AnimationCurve ac, float delta = 0, float multiplier = 1)
        {
            float val = delta + multiplier * ac.keys[0].value; if (val < 0) val *= -1;
            for (int i = 1; i < ac.keys.Length; i++)
            {
                var tmp = delta + multiplier * ac.keys[i].value;
                if (val < tmp) val = tmp;
                else if (val < -tmp) val = -tmp;
            }
            return val;
        }

        private void AddDBCol(List<MonoBehaviour> dbcList, VRCPhysBoneCollider col)
        {
            GameObject go = col.gameObject;
            var r = col.radius;
            var h = col.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule ? col.height : 0;
            int bound = col.insideBounds ? 1 : 0;
            Vector3 pos;
            int dir = 1;
            if (col.rotation == Quaternion.AngleAxis(-90f, Vector3.forward) ||
                col.rotation == Quaternion.AngleAxis(90f, Vector3.forward))
            {
                dir = 0; pos = col.position;
            }
            else if (col.rotation == Quaternion.identity ||
                col.rotation == Quaternion.AngleAxis(180f, Vector3.forward))
            {
                dir = 1; pos = col.position;
            }
            else if (col.rotation == Quaternion.AngleAxis(90f, Vector3.right) ||
                col.rotation == Quaternion.AngleAxis(-90f, Vector3.right))
            {
                dir = 2; pos = col.position;
            }
            else
            {
                go = AddGO(col.transform, Vector3.zero, col.rotation, "DynBone_Collider");
                pos = Quaternion.Inverse(col.rotation) * col.position;
            }

            var newCol = (MonoBehaviour)go.AddComponent(DynBoneColliderT);
            newCol.enabled = col.enabled;
            DynBoneColliderT.GetField("m_Center").SetValue(newCol, pos);
            DynBoneColliderT.GetField("m_Direction").SetValue(newCol, dir);
            DynBoneColliderT.GetField("m_Bound").SetValue(newCol, bound);
            DynBoneColliderT.GetField("m_Radius").SetValue(newCol, r);
            DynBoneColliderT.GetField("m_Height").SetValue(newCol, h);
            dbcList.Add(newCol);
        }

        private void AddDBPlaneCol(List<MonoBehaviour> dbcList, VRCPhysBoneCollider col)
        {
            GameObject go = col.gameObject;
            Vector3 pos = Vector3.zero;
            int dir = 1; int bound = 0;
            if (col.rotation == Quaternion.AngleAxis(-90f, Vector3.forward))
            {
                dir = 0; pos = col.position;
            }
            else if (col.rotation == Quaternion.AngleAxis(90f, Vector3.forward))
            {
                dir = 0; pos = col.position;
                bound = 1;
            }
            else if (col.rotation == Quaternion.identity)
            {
                dir = 1; pos = col.position;
            }
            else if (col.rotation == Quaternion.AngleAxis(180f, Vector3.forward))
            {
                dir = 1; pos = col.position;
                bound = 1;
            }
            else if (col.rotation == Quaternion.AngleAxis(90f, Vector3.right))
            {
                dir = 2; pos = col.position;
            }
            else if (col.rotation == Quaternion.AngleAxis(-90f, Vector3.right))
            {
                dir = 2; pos = col.position;
                bound = 1;
            }
            else
            {
                go = AddGO(col.transform, Vector3.zero, col.rotation, "DynBone_PlaneCollider");
                pos = Quaternion.Inverse(col.rotation) * col.position;
            }

            var newPlane = (MonoBehaviour)go.AddComponent(DynBonePlaneColliderT);
            newPlane.enabled = col.enabled;
            DynBonePlaneColliderT.GetField("m_Center").SetValue(newPlane, pos);
            DynBonePlaneColliderT.GetField("m_Direction").SetValue(newPlane, dir);
            DynBonePlaneColliderT.GetField("m_Bound").SetValue(newPlane, bound);
            dbcList.Add(newPlane);
        }

        private GameObject AddGO(Transform root, Vector3 position, Quaternion rotation, string GOname)
        {
            GameObject newGO = new GameObject();
            newGO.transform.parent = root;
            newGO.transform.localPosition = position;
            newGO.transform.localScale = Vector3.one;
            newGO.transform.localRotation = rotation;
            if (root.childCount == 0)
            {
                newGO.name = GOname;
            }
            else
            {
                newGO.name = GetUniqueName(newGO, GOname);
            }
            return newGO;
        }

        private void MakeDuplicate()
        {
            GameObject tmp = GameObject.Instantiate(ToConvert);
            tmp.name = GetUniqueName(ToConvert, ToConvert.name + " (DynBones)");
            if (ToConvert.transform.parent) tmp.transform.parent = ToConvert.transform.parent;
            tmp.transform.localPosition = ToConvert.transform.localPosition;
            tmp.transform.localScale = ToConvert.transform.localScale;
            tmp.transform.localRotation = ToConvert.transform.localRotation;
            ToConvert.SetActive(false); tmp.SetActive(true);
            ToConvert = tmp;
        }

        private string GetUniqueName(GameObject GO, string baseName)
        {
            List<string> GOnames = new List<string>() { "" };
            var rootT = GO.transform.parent;
            if (rootT)
            {
                foreach (Transform t in rootT)
                {
                    if (!GOnames.Contains(t.name)) GOnames.Add(t.name);
                }
            }
            else
            {
                foreach (GameObject go in GO.scene.GetRootGameObjects())
                {
                    if (!GOnames.Contains(go.name)) GOnames.Add(go.name);
                }
            }
            GOnames.Remove(GO.name);
            return ObjectNames.GetUniqueName(GOnames.ToArray(), baseName);
        }

        private void OnDestroy()
        {
            FacsGUIStyles = null;
            ToConvert = null;
            output_print = null;
            MaxAngleToStiff = null;
            pbcList = null; dbcList = null;
        }

        private static float AverageWorldBoneLength(VRCPhysBone physBone)
        {
            float num = 0f;
            if (physBone.bones.Count <= 0) return 0f;
            int num2 = 0;
            for (int i = 0; i < physBone.bones.Count; i++)
            {
                VRCPhysBoneBase.Bone bone = physBone.bones[i];
                if (bone.childIndex >= 0)
                {
                    VRCPhysBoneBase.Bone bone2 = physBone.bones[bone.childIndex];
                    num += Vector3.Distance(bone.transform.position, bone2.transform.position);
                    num2++;
                }
                else if (bone.isEndBone && physBone.endpointPosition != Vector3.zero)
                {
                    num += Vector3.Distance(bone.transform.position, bone.transform.TransformPoint(physBone.endpointPosition));
                    num2++;
                }
            }
            if (num2 <= 0) return 0f;
            return num / (float)num2;
        }

        private static void InitConversionTables()
        {
            if (!PhysBoneMigration.HasInitDBConversionTables)
            {
                PhysBoneMigration.HasInitDBConversionTables = true;
                PhysBoneMigration.StiffToMaxAngle = new AnimationCurve(new Keyframe[]
                {
                    new Keyframe(0f, 180f),
                    new Keyframe(0.1f, 129f),
                    new Keyframe(0.2f, 106f),
                    new Keyframe(0.3f, 89f),
                    new Keyframe(0.4f, 74f),
                    new Keyframe(0.5f, 60f),
                    new Keyframe(0.6f, 47f),
                    new Keyframe(0.7f, 35f),
                    new Keyframe(0.8f, 23f),
                    new Keyframe(0.9f, 11f),
                    new Keyframe(1f, 0f)
                });
                for (int i = 0; i < PhysBoneMigration.StiffToMaxAngle.length; i++)
                {
                    PhysBoneMigration.StiffToMaxAngle.SmoothTangents(i, 0f);
                }
            }

            var mats = new Keyframe[1801];
            for (int i = 0; i < mats.Length; i++)
            {
                float n = i / (mats.Length - 1f);
                mats[i] = new Keyframe(PhysBoneMigration.StiffToMaxAngle.Evaluate(n),n);
            }
            MaxAngleToStiff = new AnimationCurve(mats);
        }
    }
}
#endif