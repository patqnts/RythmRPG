using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class WireRatKingIdle
{
    public static object Main()
    {
        const string art="Assets/Art/Sprites/Character/Rat King Idle.aseprite";
        const string prefabPath="Assets/Prefab/WHO.prefab";
        var assets=AssetDatabase.LoadAllAssetsAtPath(art);
        var clip=assets.OfType<AnimationClip>().Single(c=>c.name=="Idle_Breathing");
        var controller=assets.OfType<RuntimeAnimatorController>().Single();
        var bindings=AnimationUtility.GetObjectReferenceCurveBindings(clip);
        if(bindings.Length!=1 || bindings[0].path!="" || bindings[0].propertyName!="m_Sprite")
            throw new Exception("Idle must animate only the visual's sprite.");
        if(AnimationUtility.GetCurveBindings(clip).Length!=0)
            throw new Exception("Unexpected transform curves in idle.");
        var keys=AnimationUtility.GetObjectReferenceCurve(clip,bindings[0]);
        if(keys.Any(k=>k.value==null)) throw new Exception("Missing frame sprite.");
        if(!AnimationUtility.GetAnimationClipSettings(clip).loopTime || Math.Abs(clip.length-2.4f)>.001f)
            throw new Exception("Loop settings differ from source.");
        var first=(Sprite)keys[0].value;
        var stage=PrefabStageUtility.GetCurrentPrefabStage();
        if(stage!=null && stage.assetPath==prefabPath)
            throw new Exception("WHO is open in Prefab Mode; do not replace its in-progress edits.");

        // Check actual Unity clip sampling in a disposable scene, including stable
        // pivot, unchanged transform and each of the 24 frame references.
        var preview=EditorSceneManager.NewPreviewScene();
        int checkedFrames=0;
        try
        {
            var test=(GameObject)PrefabUtility.InstantiatePrefab(assets.OfType<GameObject>().Single(),preview);
            var renderer=test.GetComponent<SpriteRenderer>();
            var testAnimator=test.GetComponent<Animator>();
            testAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            testAnimator.Rebind();
            testAnimator.Update(0);
            test.transform.localPosition=new Vector3(2,3,4);
            test.transform.localRotation=Quaternion.Euler(35,0,0);
            test.transform.localScale=new Vector3(2,2,2);
            var originalMatrix=test.transform.localToWorldMatrix;
            float ground=((Sprite)keys[0].value).bounds.min.y;
            for(int i=0;i<24;i++)
            {
                testAnimator.Play("Idle_Breathing",0,(i*.1f+.025f)/clip.length);
                testAnimator.Update(0);
                if(renderer.sprite!=keys[i].value) throw new Exception("Frame sampling failed at "+i+": "+(renderer.sprite==null?"null":renderer.sprite.name)+" expected "+keys[i].value.name);
                if(test.transform.localToWorldMatrix!=originalMatrix) throw new Exception("Idle moved root transform.");
                if(Math.Abs(renderer.sprite.bounds.min.y-ground)>.00001f) throw new Exception("Foot pivot moved.");
                checkedFrames++;
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        var root=PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var visual=root.transform.Find("Rat King");
            if(visual==null) throw new Exception("WHO has no Rat King visual.");
            var renderer=visual.GetComponent<SpriteRenderer>();
            if(renderer==null) throw new Exception("Rat King has no SpriteRenderer.");
            var animator=visual.GetComponent<Animator>();
            if(animator!=null && animator.runtimeAnimatorController!=null && animator.runtimeAnimatorController!=controller)
                throw new Exception("Rat King already has a different controller; not replacing it.");
            if(animator==null) animator=visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController=controller;
            animator.applyRootMotion=false;
            renderer.sprite=first;
            PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        int updatedLive=0;
        var active=SceneManager.GetActiveScene();
        foreach(var rootGo in active.GetRootGameObjects().Where(g=>g.name=="WHO"))
        {
            var visual=rootGo.transform.Find("Rat King");
            if(visual==null) continue;
            var renderer=visual.GetComponent<SpriteRenderer>();
            var animator=visual.GetComponent<Animator>();
            if(animator==null) animator=Undo.AddComponent<Animator>(visual.gameObject);
            if(animator.runtimeAnimatorController!=controller)
            {
                if(animator.runtimeAnimatorController!=null) throw new Exception("Live visual has a different controller.");
                Undo.RecordObject(animator,"Set Rat King breathing idle");
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            }
            if(renderer.sprite!=first)
            {
                Undo.RecordObject(renderer,"Set Rat King idle pose");renderer.sprite=first;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            updatedLive++;
        }
        return new {checkedFrames,clipLength=clip.length,loop=true,rootMotion=false,
            modifiedPrefab=prefabPath,liveVisuals=updatedLive,scene=active.path,sceneSaved=false};
    }
}
