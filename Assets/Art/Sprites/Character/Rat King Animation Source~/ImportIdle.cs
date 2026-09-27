using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Aseprite;
using UnityEditor.U2D.Sprites;

public static class ImportRatKingIdle
{
    public static object Main()
    {
        const string path="Assets/Art/Sprites/Character/Rat King Idle.aseprite";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=AssetImporter.GetAtPath(path) as AsepriteImporter;
        if(importer==null) throw new Exception("Aseprite importer is unavailable.");
        var factory=new SpriteDataProviderFactories(); factory.Init();
        var provider=factory.GetSpriteEditorDataProviderFromObject(importer);
        if(provider==null) throw new Exception("Sprite data provider unavailable.");
        provider.InitSpriteEditorDataProvider();
        var capability=provider.GetDataProvider<ISpriteFrameEditCapability>();
        if(capability==null || !capability.GetEditCapability().HasCapability(EEditCapability.EditPivot))
            throw new Exception("Importer cannot edit pivots; aborting.");
        importer.importMode=FileImportModes.AnimatedSprite;
        importer.layerImportMode=LayerImportModes.MergeFrame;
        importer.includeHiddenLayers=false;
        importer.generateAnimationClips=true;
        importer.generateModelPrefab=true;
        importer.generatePhysicsShape=false;
        importer.spritePixelsPerUnit=100;
        importer.spriteMeshType=SpriteMeshType.FullRect;
        importer.pivotSpace=PivotSpaces.Canvas;
        importer.pivotAlignment=SpriteAlignment.BottomCenter;
        importer.filterMode=FilterMode.Point;
        importer.mipmapEnabled=false;
        importer.wrapMode=TextureWrapMode.Clamp;
        var platform=importer.GetImporterPlatformSettings(EditorUserBuildSettings.activeBuildTarget);
        platform.overridden=true;
        platform.format=TextureImporterFormat.RGBA32;
        platform.textureCompression=TextureImporterCompression.Uncompressed;
        platform.maxTextureSize=2048;
        importer.SetImporterPlatformSettings(platform);
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        var assets=AssetDatabase.LoadAllAssetsAtPath(path);
        return new {
            assets=assets.Select(x=>new {x.name,type=x.GetType().Name}).ToArray(),
            clips=assets.OfType<AnimationClip>().Select(c=>new {
                c.name,c.length,c.frameRate,
                loop=AnimationUtility.GetAnimationClipSettings(c).loopTime,
                curves=AnimationUtility.GetObjectReferenceCurveBindings(c).Select(b=>new {
                    b.path,b.propertyName,type=b.type.Name,
                    keys=AnimationUtility.GetObjectReferenceCurve(c,b).Select(k=>new {k.time,value=k.value==null?null:k.value.name}).ToArray()
                }).ToArray(),
                floatBindings=AnimationUtility.GetCurveBindings(c).Select(b=>b.path+"/"+b.propertyName).ToArray()
            }).ToArray(),
            sprites=assets.OfType<Sprite>().Select(s=>new {s.name,pivot=s.pivot.ToString(),rect=s.rect.ToString(),s.pixelsPerUnit}).ToArray()
        };
    }
}
