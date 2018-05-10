﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MinorShift.Emuera.Content;

internal static class SpriteManager
{
    static float kPastTime = 300.0f;

    internal class SpriteInfo : IDisposable
    {
        internal SpriteInfo(TextureInfo p, Sprite s)
        {
            parent = p;
            sprite = s;
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(sprite);
            sprite = null;
        }
        internal Sprite sprite;
        internal TextureInfo parent;
    }
    internal class TextureInfo : IDisposable
    {
        internal TextureInfo(string b, Texture2D tex)
        {
            imagename = b;
            texture = tex;
            pasttime = Time.unscaledTime + kPastTime;
        }
        internal SpriteInfo GetSprite(CroppedImage src)
        {
            SpriteInfo sprite = null;
            if(!sprites.TryGetValue(src.Name, out sprite))
            {
                sprite = new SpriteInfo(this, 
                    Sprite.Create(texture,
                        GenericUtils.ToUnityRect(src.Rectangle, texture.width, texture.height),
                        Vector2.zero)
                    );
                sprites[src.Name] = sprite;
            }
            if(sprite != null)
                refcount += 1;
            return sprite;
        }
        internal void Release()
        {
            refcount -= 1;
            pasttime = Time.unscaledTime + kPastTime;
        }
        public void Dispose()
        {
            foreach(var kv in sprites)
            {
                kv.Value.Dispose();
            }
            sprites.Clear();
            sprites = null;

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
        internal string imagename = null;
        internal int refcount = 0;
        internal float pasttime = 0;
        internal float width { get { return texture.width; } }
        internal float height { get { return texture.height; } }
        internal Texture2D texture = null;
        Dictionary<string, SpriteInfo> sprites = new Dictionary<string, SpriteInfo>();
    }
    class CallbackInfo
    {
        public CallbackInfo(CroppedImage src, object obj, 
                            Action<object, SpriteInfo> callback)
        {
            this.src = src;
            this.obj = obj;
            this.callback = callback;
        }
        public void DoCallback(SpriteInfo info)
        {
            callback(obj, info);
        }
        public CroppedImage src;
        object obj;
        Action<object, SpriteInfo> callback;
    }

    public static void Init()
    {
#if UNITY_EDITOR
        kPastTime = 300.0f;
        GenericUtils.StartCoroutine(Update());
#else
        var memorysize = SystemInfo.systemMemorySize;
        if(memorysize <= 4096)
        {
            kPastTime = 300.0f;
            GenericUtils.StartCoroutine(Update());
        }
        else if(memorysize <= 8192)
        {
            kPastTime = 600.0f;
            GenericUtils.StartCoroutine(Update());
        }
        else
        {
            //
        }
#endif
    }
    public static void GetSprite(CroppedImage src, 
                                object obj, Action<object, SpriteInfo> callback)
    {
        var basename = src.BaseImage.Name;
        TextureInfo ti = null;
        texture_dict.TryGetValue(basename, out ti);
        if(ti == null)
        {
            var item = new CallbackInfo(src, obj, callback);
            List<CallbackInfo> list = null;
            if(loading_set.TryGetValue(basename, out list))
                list.Add(item);
            else
            {
                list = new List<CallbackInfo> { item };
                loading_set.Add(basename, list);
                GenericUtils.StartCoroutine(Loading(src.BaseImage));
            }
        }
        else
            callback(obj, GetSpriteInfo(ti, src));
    }

    public static TextureInfo GetTextureInfo(string name, string filename)
    {
        TextureInfo ti = null;
        if(texture_dict.TryGetValue(name, out ti))
            return ti;
        if(string.IsNullOrEmpty(filename))
            return null;

        FileInfo fi = new FileInfo(filename);
        if(!fi.Exists)
            return null;

        FileStream fs = fi.OpenRead();
        var filesize = fs.Length;
        byte[] content = new byte[filesize];
        fs.Read(content, 0, (int)filesize);

        TextureFormat format = TextureFormat.DXT1;
        if(uEmuera.Utils.GetSuffix(filename).ToLower() == "png")
            format = TextureFormat.DXT5;

        var tex = new Texture2D(4, 4, format, false);
        if(tex.LoadImage(content))
        {
            ti = new TextureInfo(name, tex);
            texture_dict.Add(name, ti);
        }
        return ti;
    }

    static IEnumerator Loading(BaseImage baseimage)
    {
        TextureInfo ti = null;
        FileInfo fi = new FileInfo(baseimage.Filepath);
        if(fi.Exists)
        {
            FileStream fs = fi.OpenRead();
            var filesize = fs.Length;
            byte[] content = new byte[filesize];

            var async = fs.BeginRead(content, 0, (int)filesize, null, null);
            while(!async.IsCompleted)
                yield return null;

            //TextureFormat format = TextureFormat.RGB24;
            TextureFormat format = TextureFormat.DXT1;
            if(uEmuera.Utils.GetSuffix(baseimage.Filepath).ToLower() == "png")
                format = TextureFormat.DXT5;
                //format = TextureFormat.ARGB32;

            var tex = new Texture2D(4, 4, format, false);
            if(tex.LoadImage(content))
            {
                ti = new TextureInfo(baseimage.Name, tex);
                texture_dict.Add(baseimage.Name, ti);
            }
        }
        var list = loading_set[baseimage.Name];
        foreach(var item in list)
        {
            item.DoCallback(GetSpriteInfo(ti, item.src));
        }
        list.Clear();
        loading_set.Remove(baseimage.Name);
    }
    static SpriteInfo GetSpriteInfo(TextureInfo textinfo, CroppedImage src)
    {
        return textinfo.GetSprite(src);
    }
    internal static void GivebackSpriteInfo(SpriteInfo info)
    {
        if(info == null)
            return;
        info.parent.Release();
    }
    static IEnumerator Update()
    {
        while(true)
        {
            do
            {
                yield return new WaitForSeconds(15.0f);
            } while(texture_dict.Count == 0);

            var now = Time.unscaledTime;
            TextureInfo tinfo = null;
            foreach(var ti in texture_dict.Values)
            {
                if(ti.refcount == 0 && now > ti.pasttime)
                {
                    tinfo = ti;
                    break;
                }
            }
            if(tinfo != null)
            {
                Debug.Log("Unload Texture " + tinfo.imagename);

                tinfo.Dispose();
                texture_dict.Remove(tinfo.imagename);
                tinfo = null;

                GC.Collect();
            }
        }
    }
    internal static void ForceClear()
    {
        foreach(var ti in texture_dict.Values)
        {
            ti.Dispose();
        }
        texture_dict.Clear();
        GC.Collect();
    }
    internal static void SetResourceCSVLine(string filename, string[] lines)
    {
        var cache = string.Join("\n", lines);
        UnityEngine.PlayerPrefs.SetInt(filename + "_fixed", 1);
        UnityEngine.PlayerPrefs.SetString(filename + "_time",
                        File.GetLastWriteTime(filename).ToString());
        UnityEngine.PlayerPrefs.SetString(filename, cache);
    }
    internal static string[] GetResourceCSVLines(string filename)
    {
        if(PlayerPrefs.GetInt(filename + "_fixed", 0) == 0)
            return null;
        var oldwritetime = PlayerPrefs.GetString(filename + "_time", null);
        if(string.IsNullOrEmpty(oldwritetime))
            return null;
        var writetime = File.GetLastWriteTime(filename).ToString();
        if(oldwritetime != writetime)
            return null;
        var cache = UnityEngine.PlayerPrefs.GetString(filename, null);
        if(string.IsNullOrEmpty(cache))
            return null;
        return cache.Split('\n');
    }
    internal static void ClearResourceCSVLines(string filename)
    {
        UnityEngine.PlayerPrefs.SetInt(filename + "_fixed", 0);
        UnityEngine.PlayerPrefs.SetString(filename + "_time", null);
        UnityEngine.PlayerPrefs.SetString(filename, null);
    }
    static Dictionary<string, List<CallbackInfo>> loading_set =
        new Dictionary<string, List<CallbackInfo>>();
    static Dictionary<string, TextureInfo> texture_dict =
        new Dictionary<string, TextureInfo>();
}