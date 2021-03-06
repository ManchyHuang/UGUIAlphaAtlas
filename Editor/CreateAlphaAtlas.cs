﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.U2D;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.Callbacks;

public class CreateAlphaAtlas
{
    class Entry
    {
        public Sprite sprite;
        public Texture2D texture;
        public Vector2[] uvs;
        public string atlasName;
        public Texture2D atlasTexture;
        public Vector2[] atlasUvs;
    }

    /// <summary>
    /// 生成透明通道图集
    /// </summary>
    [MenuItem("Tools/AlphaAltas/CreateAlphaAtlas")]
    public static void CreateAndSaveToDisk()
    {
        Packer.SelectedPolicy = typeof(CustomPackerPolicy).Name;
        CustomPackerPolicy.forceIOSOpaque = true;
        Packer.RebuildAtlasCacheIfNeeded(BuildTarget.iOS, true, Packer.Execution.ForceRegroup);

        Dictionary<string, Texture2D> atlasTextures = CreateAlphaAtlasTexture();

        string basePath = Path.Combine(Application.dataPath, AlphaAtlasManager.TEXTURE_ALPHA_ATLAS_PATH);
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);

        foreach (var pair in atlasTextures)
        {
            string outPath = Path.Combine(basePath, pair.Key + "_alpha.png");
            File.WriteAllBytes(outPath, pair.Value.EncodeToPNG());
        }

        AlphaAtlasManager atlasConfig = ScriptableObject.CreateInstance<AlphaAtlasManager>();
        atlasConfig.names = new List<string>(atlasTextures.Keys);
        AssetDatabase.CreateAsset(atlasConfig, Path.Combine("Assets/" + AlphaAtlasManager.TEXTURE_ALPHA_ATLAS_PATH, "AlphaAtlasConfig.asset"));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static Dictionary<string,Texture2D> CreateAlphaAtlasTexture()
    {
        Dictionary<string, Texture2D> result = new Dictionary<string, Texture2D>();

        List<Entry> entries = new List<Entry>();

        Material mat = new Material(Shader.Find("Unlit/Transparent"));
        
        List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        foreach (string path in AssetDatabase.FindAssets("t:sprite"))
        {
            objects.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GUIDToAssetPath(path)));
        }

        Sprite[] sprites = objects.Distinct()
                    .Select(x => x as Sprite)
                    .Where(x => x != null && x.packed)
                    .ToArray();
        foreach (Sprite sprite in sprites)
        {
            string atlasName;
            Texture2D atlasTexture;
            Packer.GetAtlasDataForSprite(sprite, out atlasName, out atlasTexture);
            Texture2D texture = SpriteUtility.GetSpriteTexture(sprite, false);
            if (atlasTexture != null && texture != null && texture.format == TextureFormat.RGBA32)
            {
                entries.Add(new Entry()
                {
                    sprite = sprite,
                    atlasName = atlasName,
                    texture = texture,
                    atlasTexture = atlasTexture,
                    uvs = SpriteUtility.GetSpriteUVs(sprite, false),
                    atlasUvs = SpriteUtility.GetSpriteUVs(sprite, true),
                });
            }
        }

        var atlasGroups =
            from e in entries
            group e by e.atlasTexture;

        foreach (var atlasGroup in atlasGroups)
        {
            Texture tex = atlasGroup.Key;

            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.SetRenderTarget(rt);
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            GL.LoadOrtho();

            foreach (var entry in atlasGroup)
            {
                mat.mainTexture = entry.texture;
                mat.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                var tris = entry.sprite.triangles;
                foreach (int index in tris)
                {
                    GL.TexCoord(entry.uvs[index]);
                    GL.Vertex(entry.atlasUvs[index]);
                }
                GL.End();
            }
            GL.PopMatrix();

            Texture2D tex2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            tex2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            var colors = tex2.GetPixels32();

            int count = colors.Length;
            Color32[] newColors = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                byte alpha = colors[i].a;
                newColors[i] = new Color32(alpha, alpha, alpha, 255);
            }
            tex2.SetPixels32(newColors);
            tex2.Apply();

            string texName = tex.name;
            texName = tex.name.Substring(0, texName.LastIndexOf("-")) + "-fmt32";

            result.Add(texName, tex2);
            RenderTexture.ReleaseTemporary(rt);
        }

        return result;
    }

    /// <summary>
    /// 将透明图集直接打入AssetBoundles内，和系统默认ETC1+Alpha效果一样，可以直接用Image显示不需要SplitImage，而且之前生成的文件不需要再手动打入包内
    /// </summary>
    [MenuItem("Tools/AlphaAltas/PackAlphaAltasToAssetBoundles")]
    
    public static void PackAlphaAltasToAssetBoundles()
    {
        EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOn;
        Packer.SelectedPolicy = typeof(CustomPackerPolicy).Name;
        CustomPackerPolicy.forceIOSOpaque = true;
        Packer.RebuildAtlasCacheIfNeeded(BuildTarget.iOS, true, Packer.Execution.ForceRegroup);

        List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        foreach (string path in AssetDatabase.FindAssets("t:sprite"))
        {
            objects.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GUIDToAssetPath(path)));
        }

        Sprite[] sprites = objects.Distinct()
                    .Select(x => x as Sprite)
                    .Where(x => x != null && x.packed)
                    .ToArray();

        List<SerializedObject> sos = new List<SerializedObject>();
        foreach (Sprite sprite in sprites)
        {
            Texture2D atlasTexture;
            string atlasName;
            Packer.GetAtlasDataForSprite(sprite, out atlasName, out atlasTexture);
            if (atlasTexture != null)
            {
                SerializedObject so = new SerializedObject(sprite);
                so.FindProperty("m_RD.textureRect").rectValue = GetAltasTextureRect(sprite, atlasTexture);
                so.FindProperty("m_RD.texture").objectReferenceValue = atlasTexture;
                Texture2D alphaTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/" + AlphaAtlasManager.TEXTURE_ALPHA_ATLAS_PATH + atlasTexture.name + "_alpha.png");
                so.FindProperty("m_RD.alphaTexture").objectReferenceValue = alphaTexture;
                so.ApplyModifiedProperties();

                sos.Add(so);
            }
        }

        EditorSettings.spritePackerMode = SpritePackerMode.Disabled;
        BuildPipeline.BuildAssetBundles(Application.dataPath + "/AssetBundles", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.iOS);
        EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOn;

        foreach (SerializedObject so in sos)
        {
            Sprite sprite =  so.targetObject as Sprite;
            so.FindProperty("m_RD.textureRect").rectValue = sprite.textureRect;
            so.FindProperty("m_RD.texture").objectReferenceValue = SpriteUtility.GetSpriteTexture(sprite, false);
            so.FindProperty("m_RD.alphaTexture").objectReferenceValue = null;
            so.ApplyModifiedProperties();
        }
        AssetDatabase.Refresh();
    }

    static Rect GetAltasTextureRect(Sprite sp, Texture2D atlasTexture)
    {
        Rect textureRect = new Rect();
        Vector2[] uvs = SpriteUtility.GetSpriteUVs(sp, false);
        Vector2[] altasUvs = SpriteUtility.GetSpriteUVs(sp, true);
        int count = uvs.Length;
        int compare = 0;
        for (int i = 1;i < count;i++)
        {
            if (uvs[i].x != uvs[0].x && uvs[i].y != uvs[0].y)
            {
                compare = i;
                break;
            }
        }
        textureRect.width = (altasUvs[0].x - altasUvs[compare].x) / (uvs[0].x - uvs[compare].x);
        textureRect.height = (altasUvs[0].y - altasUvs[compare].y) / (uvs[0].y - uvs[compare].y);
        textureRect.x = altasUvs[0].x - textureRect.width * uvs[0].x;
        textureRect.y = altasUvs[0].y - textureRect.height * uvs[0].y;
        textureRect.x *= atlasTexture.width;
        textureRect.y *= atlasTexture.height;
        textureRect.width *= atlasTexture.width;
        textureRect.height *= atlasTexture.height;
        return textureRect;
    }
}
