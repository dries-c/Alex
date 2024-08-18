﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Alex.Interfaces;
using Alex.Interfaces.Resources;
using Alex.ResourcePackLib.Abstraction;
using Alex.ResourcePackLib.Generic;
using Alex.ResourcePackLib.IO.Abstract;
using Alex.ResourcePackLib.Json;
using Alex.ResourcePackLib.Json.BlockStates;
using Alex.ResourcePackLib.Json.Fonts;
using Alex.ResourcePackLib.Json.Models;
using Alex.ResourcePackLib.Json.Sound;
using Alex.ResourcePackLib.Json.Textures;
using Newtonsoft.Json;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using IColor = Alex.Interfaces.IColor;
using Image = SixLabors.ImageSharp.Image;

namespace Alex.ResourcePackLib
{
    public class MCJavaResourcePack : ResourcePack, ITextureProvider, IBlockStateResourceProvider, IFontSourceProvider, IDisposable
    {
        public static string[] BitmapFontCharacters = new string[]
        {
            "\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000",
            "\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000",
            "\u0020\u0021\u0022\u0023\u0024\u0025\u0026\u0027\u0028\u0029\u002a\u002b\u002c\u002d\u002e\u002f",
            "\u0030\u0031\u0032\u0033\u0034\u0035\u0036\u0037\u0038\u0039\u003a\u003b\u003c\u003d\u003e\u003f",
            "\u0040\u0041\u0042\u0043\u0044\u0045\u0046\u0047\u0048\u0049\u004a\u004b\u004c\u004d\u004e\u004f",
            "\u0050\u0051\u0052\u0053\u0054\u0055\u0056\u0057\u0058\u0059\u005a\u005b\u005c\u005d\u005e\u005f",
            "\u0060\u0061\u0062\u0063\u0064\u0065\u0066\u0067\u0068\u0069\u006a\u006b\u006c\u006d\u006e\u006f",
            "\u0070\u0071\u0072\u0073\u0074\u0075\u0076\u0077\u0078\u0079\u007a\u007b\u007c\u007d\u007e\u0000",
            "\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000",
            "\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u00a3\u0000\u0000\u0192",
            "\u0000\u0000\u0000\u0000\u0000\u0000\u00aa\u00ba\u0000\u0000\u00ac\u0000\u0000\u0000\u00ab\u00bb",
            "\u2591\u2592\u2593\u2502\u2524\u2561\u2562\u2556\u2555\u2563\u2551\u2557\u255d\u255c\u255b\u2510",
            "\u2514\u2534\u252c\u251c\u2500\u253c\u255e\u255f\u255a\u2554\u2569\u2566\u2560\u2550\u256c\u2567",
            "\u2568\u2564\u2565\u2559\u2558\u2552\u2553\u256b\u256a\u2518\u250c\u2588\u2584\u258c\u2590\u2580",
            "\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u2205\u2208\u0000",
            "\u2261\u00b1\u2265\u2264\u2320\u2321\u00f7\u2248\u00b0\u2219\u0000\u221a\u207f\u00b2\u25a0\u0000"
        };

        private const RegexOptions RegexOpts =
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        private static readonly Regex IsLanguageResource = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)((?'namespace'.*)(?<!realms))[\\\/]lang[\\\/](?'filename'.*)[\\.](?'filetype'json|lang)$",
            RegexOpts);

        private static readonly Regex IsFontTextureResource = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]textures[\\\/]font[\\\/](?'filename'.*)\.png$",
            RegexOpts);

        private static readonly Regex IsTextureResource = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]textures[\\\/](?'filename'.*)\.png$", RegexOpts);

        private static readonly Regex IsTextureMetaResource = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]textures[\\\/](?'filename'.*)\.png.mcmeta$",
            RegexOpts);

        private static readonly Regex IsModelRegex = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]models[\\\/](?'filename'.*)\.json$", RegexOpts);

        private static readonly Regex IsBlockStateRegex = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]blockstates[\\\/](?'filename'.*)\.json$",
            RegexOpts);

        private static readonly Regex IsGlyphSizes = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]font[\\\/]glyph_sizes.bin$", RegexOpts);

        private static readonly Regex IsFontDefinition = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]font[\\\/](?'filename'.*)\.json$", RegexOpts);

        private static readonly Regex IsParticle = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]particles[\\\/](?'filename'.*)\.json$", RegexOpts);

        private static readonly Regex IsSoundDefinition = new Regex(
            @"^(?(?=assets[\\\/])assets[\\\/]|)(?'namespace'.*)[\\\/]sounds.json$", RegexOpts);

        private readonly Dictionary<ResourceLocation, Lazy<BlockStateResource>> _blockStates = new Dictionary<ResourceLocation, Lazy<BlockStateResource>>();
        public Dictionary<ResourceLocation, ResourcePackModelBase> Models = new Dictionary<ResourceLocation, ResourcePackModelBase>();
        private readonly Dictionary<ResourceLocation, Func<Image<Rgba32>>> _bitmapCache = new Dictionary<ResourceLocation, Func<Image<Rgba32>>>();
        private readonly Dictionary<ResourceLocation, TextureMeta> _textureMetaCache = new Dictionary<ResourceLocation, TextureMeta>();
        private readonly Dictionary<string, LanguageResource> _languageCache = new Dictionary<string, LanguageResource>(StringComparer.OrdinalIgnoreCase);

        private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(MCJavaResourcePack));

        //public IReadOnlyDictionary<ResourceLocation, TextureMeta> TextureMetas => _textureMetaCache;
        public IReadOnlyDictionary<string, LanguageResource> Languages => _languageCache;

        //public new ResourcePackInfo Info { get; private set; }

        //public IFont Font { get; private set; }

        public bool IsPreLoaded { get; private set; }
        public bool IsLoaded { get; private set; }

        private McResourcePackPreloadCallback PreloadCallback { get; } = null;

        private byte[] GlyphWidth = null;

        private IColor[] FoliageColors { get; set; } = null;
        private int _foliageWidth = 256;
        private int _foliageHeight = 256;

        private IColor[] GrassColors { get; set; } = null;
        private int _grassHeight = 256;
        private int _grassWidth = 256;

        private PngDecoder PngDecoder { get; }
        public IDictionary<string, SoundDefinition> SoundDefinitions { get; private set; }

        private IFilesystem Filesystem { get; set; }

        public MCJavaResourcePack(IFilesystem archive,
            McResourcePackPreloadCallback preloadCallback,
            ResourcePackManifest manifest,
            LoadProgress progressReporter = null)
        {
            Info = manifest;
            Filesystem = archive;
            ProgressReporter = progressReporter;

            PngDecoder = PngDecoder.Instance; // new PngDecoder() { IgnoreMetadata = true };
            PreloadCallback = preloadCallback;
            //_archive = archive;
            Load(archive);
        }


        private void Preload(IFilesystem archive)
        {
            if (IsPreLoaded) return;

            LoadMeta(archive);

            // First load the font, then we can render the splash screen!
            LoadFont(archive);

            IsPreLoaded = true;

            //if (FontBitmap != null)
            //{
            //	PreloadCallback?.Invoke(FontBitmap, BitmapFontCharacters.ToCharArray().ToList());
            //}
        }

        private string SanitizeFilename(string filename)
        {
            return filename.Replace("\\", "/");
        }

        private void Load(IFilesystem archive)
        {
            if (!IsPreLoaded)
            {
                Preload(archive);
            }

            if (IsLoaded) return;

            //if (Info == null)
            //{
            //	Info = GetManifest(archive, ResourcePackType.Java);
            //}

            //Dictionary<ResourceLocation, ResourcePackModelBase> models = new Dictionary<ResourceLocation, ResourcePackModelBase>();

            var total = archive.Entries.Count;
            int count = 0;

            foreach (var entry in archive.Entries)
            {
                count++;

                ProgressReporter?.Invoke((int)(((double)count / (double)total) * 100D), entry.Name);

                if (IsParticle.IsMatch(entry.FullName))
                {
                    continue;
                }

                if (IsSoundDefinition.IsMatch(entry.FullName))
                {
                    using (var e = entry.Open())
                    {
                        SoundDefinitions = SoundDefinition.FromJson(Encoding.UTF8.GetString(e.ReadToEnd()));
                    }

                    continue;
                }

                var textureMatchs = IsTextureResource.Match(entry.FullName);

                if (textureMatchs.Success)
                {
                    ProcessTexture(entry, textureMatchs);

                    continue;
                }

                var textureMetaMatch = IsTextureMetaResource.Match(entry.FullName);

                if (textureMetaMatch.Success)
                {
                    LoadTextureMeta(entry, textureMetaMatch);

                    continue;
                }

                var languageMatchs = IsLanguageResource.Match(entry.FullName);

                if (languageMatchs.Success)
                {
                    LoadLocalization(entry, languageMatchs);

                    continue;
                }

                var modelMatch = IsModelRegex.Match(entry.FullName);

                if (modelMatch.Success)
                {
                    var resourceLocation = new ResourceLocation(
                        modelMatch.Groups["namespace"].Value, SanitizeFilename(modelMatch.Groups["filename"].Value));

                    var model = ReadModel(entry, resourceLocation);

                    if (model != null)
                        Models.Add(resourceLocation, model);

                    continue;
                }

                var blockStateMatch = IsBlockStateRegex.Match(entry.FullName);

                if (blockStateMatch.Success)
                {
                    LoadBlockState(entry, blockStateMatch);

                    continue;
                }

                var glyphSizeMatch = IsGlyphSizes.Match(entry.FullName);

                if (glyphSizeMatch.Success)
                {
                    LoadGlyphSizes(entry);

                    continue;
                }
            }

            LoadColormap();

            IsLoaded = true;
        }


        private void LoadMeta(IFilesystem archive)
        {
            //Info = GetManifest(archive);
            /*ResourcePackInfo info;

            var entry = archive.GetEntry("pack.mcmeta");
            if (entry == null)
            {
                info = new ResourcePackInfo();
            }
            else
            {
                using (var stream = entry.Open())
                {
                    ResourcePackInfoWrapper wrap =
                        MCJsonConvert.DeserializeObject<ResourcePackInfoWrapper>(
                            Encoding.UTF8.GetString(stream.ReadToSpan(entry.Length)));
                    info = wrap.pack;
                }
            }

            Info = info;*/
        }

        private void LoadTextureMeta(IFile entry, Match match)
        {
            if (!TryGetTextureMeta(
                    new ResourceLocation(
                        match.Groups["namespace"].Value, SanitizeFilename(match.Groups["filename"].Value)),
                    out var meta))
            {
                LoadBitmapMeta(entry, match);
            }
        }

        private void ProcessTexture(IFile entry, Match match)
        {
            if (entry.Length == 0)
                return;

            try
            {
                LoadBitmap(
                    entry,
                    new ResourceLocation(
                        match.Groups["namespace"].Value, SanitizeFilename(match.Groups["filename"].Value)));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not load texture from resourcepack: {entry.FullName}");
            }
        }

        private Func<Image<Rgba32>> LoadBitmap(IFile entry, ResourceLocation resource)
        {
            if (_bitmapCache.TryGetValue(resource, out var result))
            {
                return result;
            }

            _bitmapCache[resource] = new Func<Image<Rgba32>>(
                () =>
                {
                    try
                    {
                        using (var s = entry.Open())
                        {
                            //img = new Bitmap(s);
                           // var data = s.ReadToSpan(entry.Length);

                            return PngDecoder.Decode<Rgba32>(new PngDecoderOptions(), s);// Image.Load<Rgba32>(data, PngDecoder);
                        }
                    }
                    catch (InvalidImageContentException)
                    {
                        return null;
                    }
                });

            return _bitmapCache[resource];
        }

        #region BitmapFont

        private FontDefinitionFile LoadFontDefinition(IFile entry)
        {
            ReadOnlySpan<byte> content;

            using (var stream = entry.Open())
            {
                content = stream.ReadToEnd();
            }

            return MCJsonConvert.DeserializeObject<FontDefinitionFile>(Encoding.UTF8.GetString(content));
        }

        private void LoadFont(IFilesystem archive)
        {
            var sources = new List<BitmapFontSource>();
            
            foreach (var entry in archive.Entries)
            {
                var fontDefinitionMatch = IsFontDefinition.Match(entry.FullName);

                if (fontDefinitionMatch.Success)
                {
                    var fontDefinitionFile = LoadFontDefinition(entry);

                    if (fontDefinitionFile?.Providers != null)
                    {
                        foreach (var definition in fontDefinitionFile?.Providers)
                        {
                            if (definition is BitmapFontDefinition bitmapFont)
                            {
                                var fileName = new ResourceLocation(bitmapFont.File);
                                var filePath = Path.Combine(fileName.Namespace, "textures", fileName.Path);
                                var bitmapEntry = archive.GetEntry(filePath);

                                if (bitmapEntry == null)
                                    continue;

                                // LoadBitmap(bitmapEntry, fileName);

                                bitmapFont.File = filePath;

                                if (LoadBitmapFontSource(
                                        bitmapEntry, bitmapFont.Characters, out var bitmapFontSource,
                                        bitmapEntry.Name.Equals("ascii.png")))
                                {
                                    sources.Add(bitmapFontSource);
                                }
                            }
                            else if (definition is LegacyFontDefinition legacyFont)
                            {
                                for (int i = 0; i < 0xFF; i++)
                                {
                                    var f = i.ToString("x2").ToLower();
                                    var fileName = new ResourceLocation(legacyFont.Template.Replace("%s", f));

                                    var filePath = Path.Combine(fileName.Namespace, "textures", fileName.Path);

                                    var bitmapEntry = archive.GetEntry(filePath);

                                    if (bitmapEntry == null)
                                        continue;

                                    //    LoadBitmap(bitmapEntry, fileName);

                                    var startChar = int.Parse($"{f}00", NumberStyles.HexNumber);

                                    var characters = Enumerable.Range(startChar, 256).Select(x => (char) x).Batch(16)
                                       .Select(x => new string(x.ToArray())).ToArray();

                                    if (LoadBitmapFontSource(bitmapEntry, characters, out var bitmapFontSource))
                                    {
                                        sources.Add(bitmapFontSource);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            FontSources = sources.ToArray();
            

            if (!DidPreload)
            {
                DidPreload = true;

                PreloadCallback?.Invoke(FontSources);
            }

        }

        private bool DidPreload { get; set; } = false;

        private bool LoadBitmapFontSource(IFile entry, string[] characters, out BitmapFontSource bitmapFontSource, bool isAscii = false)
        {
            var match = IsFontTextureResource.Match(entry.FullName);

            if (match.Success)
            {
                var fontBitmap = LoadBitmap(
                    entry,
                    new ResourceLocation(
                        match.Groups["namespace"].Value, SanitizeFilename(match.Groups["filename"].Value)));
                //ProcessTexture(entry, match);
                var bitmap = fontBitmap();
                bitmapFontSource = new BitmapFontSource(entry.Name, bitmap, characters, isAscii);

                return true;
            }

            bitmapFontSource = null;
            return false;
        }
        
        private void LoadGlyphSizes(IFile entry)
        {
            byte[] glyphWidth; // = new byte[65536];

            using (Stream stream = entry.Open())
            {
                glyphWidth = stream.ReadToSpan(entry.Length).ToArray();
                //int length = stream.Read(glyphWidth, 0, glyphWidth.Length);
                //Array.Resize(ref glyphWidth, length);
            }

            GlyphWidth = glyphWidth;
        }

        /*public bool TryLoadFont(GraphicsDevice graphicsDevice, FontDefinition definition, out IFont font)
        {
            font = null;

            if (definition is BitmapFontDefinition bitmapFont)
            {
                if (TryGetBitmap(new ResourceLocation(bitmapFont.File), out var fontBitmap))
                {
                    var chars = bitmapFont.Characters ?? BitmapFontCharacters;
                    font = new BitmapFont(graphicsDevice, fontBitmap, 16, chars);

                    return true;
                }

                //ProcessTexture(entry, match);
            }
            else if (definition is LegacyFontDefinition legacyFont)
            {
                var fileName = new ResourceLocation(legacyFont.Sizes);

                var fontSizes = Filesystem.GetEntry(Path.Combine("assets", fileName.Namespace, fileName.Path));
            }

            return false;
        }*/

        #endregion

        #region Bitmap

        private void LoadColormap()
        {
            if (TryGetBitmap("colormap/foliage", out Image<Rgba32> foliage))
            {
                FoliageColors = GetColorArray(foliage);

                _foliageHeight = foliage.Height;
                _foliageWidth = foliage.Width;
                foliage.Dispose();
            }

            if (TryGetBitmap("colormap/grass", out Image<Rgba32> grass))
            {
                GrassColors = GetColorArray(grass);

                _grassWidth = grass.Width;
                _grassHeight = grass.Height;
                grass.Dispose();
            }
        }

        private IColor[] GetColorArray(Image<Rgba32> image)
        {
            var cloned = image;

            if (cloned.DangerousTryGetSinglePixelMemory(out var pixelSpan))
            {
                return pixelSpan.ToArray().Select(x =>  Primitives.Factory.Color(x.Rgba)).ToArray();
            }

            return null;/*

            Color[] colors = new Color[cloned.Width * cloned.Height];

            for (int x = 0; x < cloned.Width; x++)
            {
                for (int y = 0; y < cloned.Height; y++)
                {
                    var indx = cloned.Width * y + x;
                    colors[indx] = new Color(cloned[x, y].Rgba);
                }
            }

            return colors;*/
        }

        private string NormalisePath(string path)
        {
            return path.Replace('\\', '/').ToLowerInvariant();
        }

        public bool TryGetBitmap(ResourceLocation textureName, out Image<Rgba32> bitmap)
        {
            if (_bitmapCache.TryGetValue(
                    new ResourceLocation(textureName.Namespace, NormalisePath(textureName.Path)), out var t))
            {
                bitmap = t();

                return bitmap != null;
            }

            bitmap = null;

            return false;
        }

        private void LoadBitmapMeta(IFile entry, Match match)
        {
            TextureMeta meta;

            using (var stream = entry.Open())
            {
                //using (StreamReader sr = new StreamReader(stream))
                {
                    //string content = sr.ReadToEnd();
                    meta = TextureMeta.FromJson(Encoding.UTF8.GetString(stream.ReadToSpan(entry.Length)));
                }
            }

            _textureMetaCache[
                new ResourceLocation(
                    match.Groups["namespace"].Value, SanitizeFilename(match.Groups["filename"].Value))] = meta;
        }

        public bool TryGetTextureMeta(ResourceLocation textureName, out TextureMeta meta)
        {
            if (_textureMetaCache.TryGetValue(textureName, out meta))
                return true;

            meta = null;

            return false;
        }

        #endregion

        #region Block States

        private void LoadBlockState(IFile entry, Match match)
        {
            try
            {
                string name = match.Groups["filename"].Value;
                string nameSpace = match.Groups["namespace"].Value;
                string key = $"{nameSpace}:{name}";

                _blockStates[key] = new Lazy<BlockStateResource>(
                    () =>
                    {
                        using (var stream = entry.Open())
                        {
                            var json = Encoding.UTF8.GetString(stream.ReadToSpan(entry.Length));

                            var blockState = MCJsonConvert.DeserializeObject<BlockStateResource>(json);
                            blockState.Name = name;
                            blockState.Namespace = nameSpace;

                            return blockState;
                        }
                    });
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Could not load {entry.Name}!");
                //	return null;
            }
        }

        public bool TryGetBlockState(ResourceLocation modelName, out BlockStateResource stateResource)
        {
            if (_blockStates.TryGetValue(modelName, out var lazy))
            {
                stateResource = lazy.Value;

                return true;
            }

            stateResource = null;

            return false;
        }

        #endregion

        #region Models

        private ResourcePackModelBase ReadModel(IFile entry, ResourceLocation location)
        {
            try
            {
                using (var stream = entry.Open())
                {
                    var model = MCJsonConvert.DeserializeObject<ResourcePackModelBase>(
                        Encoding.UTF8.GetString(stream.ReadToSpan(entry.Length)));

                    model.Type = entry.FullName.Contains("models/block/", StringComparison.InvariantCultureIgnoreCase) ? ModelType.Block : ModelType.Item;

                    if (entry.FullName.EndsWith("item/handheld.json"))
                    {
                        model.Type |= ModelType.Handheld;
                    }

                    //if (model.ParentName != null)
                    //{
                    //	model.Type = model.ParentName.Path.StartsWith("item/", StringComparison.InvariantCultureIgnoreCase) ?
                    //		ModelType.Block : ModelType.Item;
                    //}

                    return model;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error loading model: ({location})");

                return null;
            }
        }

        #endregion

        #region Localization

        private void LoadLocalization(IFile entry, Match match)
        {
            string name = match.Groups["filename"].Value;
            string nameSpace = match.Groups["namespace"].Value;

            bool isJson = match.Groups["filetype"].Value.Equals("json", StringComparison.OrdinalIgnoreCase);

            string text;

            using (var stream = entry.Open())
            {
                text = Encoding.UTF8.GetString(stream.ReadToSpan(entry.Length));
            }

            LanguageResource lang;

            if (isJson)
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(text);

                if (!dict.TryGetValue("language.code", out var cultureCode)
                    || !dict.TryGetValue("language.name", out var cultureName)
                    || !dict.TryGetValue("language.region", out var cultureRegion))
                {
                    return;
                }

                lang = new LanguageResource(dict)
                {
                    CultureCode = cultureCode, CultureName = cultureName, CultureRegion = cultureRegion
                };
                /*	if (lang.CultureCode == null)
                    {
                        if (lang.TryGetValue("language.code", out var code))
                            lang.CultureCode = code;
                    }*/
            }
            else
            {
                lang = LanguageResource.ParseLangFile(text);
            }

            lang.CultureName = name;
            //lang.Namespace   = nameSpace;

            _languageCache[$"{nameSpace}:{name}"] = lang;
        }

        #endregion

        public bool TryGetGrassColor(float temp, float rain, int elevation, out IColor color)
        {
            color = Primitives.Factory.Color(94, 157, 52);

            if (GrassColors == null) return false;

            temp = Math.Clamp(temp - elevation * 0.00166667f, 0f, 1f);
            rain = Math.Clamp(rain, 0f, 1f) * temp;

            int x = (int)Math.Floor(Math.Clamp(_grassWidth - (_grassWidth * temp), 0, _grassWidth));
            int y = (int)Math.Floor(Math.Clamp(_grassHeight - (_grassHeight * rain), 0, _grassHeight));

            var indx = _grassWidth * y + x;

            if (indx < 0) indx = 0;
            if (indx > GrassColors.Length - 1) indx = GrassColors.Length - 1;

            var result = GrassColors[indx];

            color =  Primitives.Factory.Color(result.R, result.G, result.B);

            return true;
        }

        public bool TryGetFoliageColor(float temp, float rain, int elevation, out IColor color)
        {
            color = Primitives.Factory.Color(94, 157, 52);

            if (FoliageColors == null) return false;
            temp = Math.Clamp(temp - elevation * 0.00166667f, 0f, 1f);
            rain = Math.Clamp(rain, 0f, 1f) * temp;

            int x = (int)Math.Floor(Math.Clamp(_foliageWidth - (_foliageWidth * temp), 0, _foliageWidth));
            int y = (int)Math.Floor(Math.Clamp(_foliageHeight - (_foliageHeight * rain), 0, _foliageHeight));

            var indx = _foliageWidth * y + x;

            if (indx < 0) indx = 0;
            if (indx > FoliageColors.Length - 1) indx = FoliageColors.Length - 1;

            var result = FoliageColors[indx];

            color = Primitives.Factory.Color(result.R, result.G, result.B);

            return true;
        }


        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                Filesystem?.Dispose();
                Filesystem = null;

                if (FontSources != null)
                {
                    var fontSources = FontSources.ToArray();
                    FontSources = null;

                    foreach (var fontSource in fontSources)
                    {
                        fontSource?.Image?.Dispose();
                    }
                }
            }
        }

        public BitmapFontSource[] FontSources { get; private set; }
    }

    public delegate void McResourcePackPreloadCallback(BitmapFontSource[] fontSources);
}