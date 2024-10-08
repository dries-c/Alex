﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Alex.Common.Utils;
using Alex.Interfaces;
using Alex.ResourcePackLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MiNET.Worlds;
using NLog;
using RocketUI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Size = RocketUI.Size;

namespace Alex.Common.Graphics.Typography
{
    public class BitmapFont : IFont
    {
        #region Properties

        public IReadOnlyCollection<char> Characters { get; }

        public int GridWidth { get; }
        public int GridHeight { get; }

        public int LineSpacing { get; set; } = 10;
        public int CharacterSpacing { get; set; } = 1;

        public Texture2D AsciiTexture { get; private set; }
        public Texture2D[] UnicodeTextures { get; private set; }

        public Glyph DefaultGlyph { get; private set; }

        //public Glyph[] Glyphs { get; private set; }
        private IReadOnlyDictionary<char, IFontGlyph> Glyphs { get; set; } // Dictionary<char, Glyph> Glyphs { get; }
        private Vector2 Scale { get; set; } = Vector2.One;

        private bool _isInitialised = false;

        #endregion

        #region Constructors

        private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(BitmapFont));
        public BitmapFont(GraphicsDevice graphicsDevice, BitmapFontSource[] sources)
        {
            GridWidth = 16;
            GridHeight = 16;
            
            Characters = string.Join("", sources.SelectMany(s => s.Characters)).ToCharArray();
            DefaultGlyph = new Glyph('\x0000', null, 0, 8, 1f);
            
            LoadGlyphs(graphicsDevice, sources.Where(
                x =>
                {
                    if (x.Image == null)
                    {
                        Log.Warn($"Tried loading null font: {x.Name}");
                        return false;
                    }

                    return true;
                }).ToArray());
        }

        #endregion

        public Vector2 MeasureString(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Vector2.Zero;
            }

            float width = 0.0f, finalLineHeight = LineSpacing;
            Vector2 offset = Vector2.Zero;
            var firstGlyphOfLine = true;

            var fontStyle = FontStyle.None;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r') continue;

                if (c == '\n')
                {
                    offset.X = 0.0f;
                    offset.Y += LineSpacing;

                    finalLineHeight = LineSpacing;
                    firstGlyphOfLine = true;

                    fontStyle = FontStyle.None;
                }
                else if (c == '\x00A7')
                {
                    // Formatting

                    // Get next character
                    if (i + 1 >= text.Length) continue;

                    i++;
                    var formatChar = text[i];

                    if (formatChar == 'k')
                    {
                        //Obfuscated
                    }
                    else if (formatChar == 'l')
                    {
                        fontStyle |= FontStyle.Bold;
                    }
                    else if (formatChar == 'm')
                    {
                        fontStyle |= FontStyle.StrikeThrough;
                    }
                    else if (formatChar == 'n')
                    {
                        fontStyle |= FontStyle.Underline;
                    }
                    else if (formatChar == 'o')
                    {
                        fontStyle |= FontStyle.Italic;
                    }
                    else if (formatChar == 'r')
                    {
                        fontStyle = FontStyle.None;
                    }
                }
                else
                {
                    var glyph = GetGlyphOrDefault(c);

                    if (firstGlyphOfLine)
                    {
                        firstGlyphOfLine = false;
                    }

                    offset.X += (glyph.Width) + (((fontStyle & FontStyle.Bold) != 0) ? 1 : 0) + CharacterSpacing;

                    finalLineHeight = MathF.Max(finalLineHeight, glyph.Height);
                    width = MathF.Max(width, offset.X);
                }
            }

            return new Vector2(width, offset.Y + finalLineHeight);
        }

        private void Rotate(float rotation)
        {

        }

        public void DrawString(SpriteBatch sb,
            string text,
            Vector2 position,
            Color color,
            FontStyle style = FontStyle.None,
            Vector2? scale = null,
            float opacity = 1f,
            float rotation = 0f,
            Vector2? origin = null,
            SpriteEffects effects = SpriteEffects.None,
            float layerDepth = 0f) => DrawString(
            sb, text, position, color.ToTextColor(), style, scale, opacity, rotation, origin, effects, layerDepth);

        private static char[] _colorChars = "0123456789abcdef".ToArray();
        public void DrawString(SpriteBatch sb,
            string text,
            Vector2 position,
            TextColor color,
            FontStyle style = FontStyle.None,
            Vector2? scale = null,
            float opacity = 1f,
            float rotation = 0f,
            Vector2? origin = null,
            SpriteEffects effects = SpriteEffects.None,
            float layerDepth = 0f)
        {
            if (string.IsNullOrEmpty(text)) return;

            var originVal = origin ?? Vector2.Zero;
            var scaleVal = scale ?? Vector2.One;
            //scaleVal *= Scale;

            originVal *= scaleVal;

            var flipAdjustmentX = 0f;
            var flipAdjustmentY = 0f;

            var flippedVert = (effects & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically;
            var flippedHorz = (effects & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally;

            if (flippedVert || flippedHorz)
            {
                Vector2 size = MeasureString(text);

                if (flippedHorz)
                {
                    originVal.X *= -1;
                    flipAdjustmentX = -size.X;
                }

                if (flippedVert)
                {
                    originVal.Y *= -1;
                    flipAdjustmentY = LineSpacing - size.Y;
                }
            }

            var adjustedOriginX = flipAdjustmentX - originVal.X;
            var adjustedOriginY = flipAdjustmentY - originVal.Y;

            Matrix transformation = Matrix.Identity;
            float cos = 0, sin = 0;

            if (rotation == 0)
            {
                transformation.M11 = (flippedHorz ? -scaleVal.X : scaleVal.X);
                transformation.M22 = (flippedVert ? -scaleVal.Y : scaleVal.Y);
                transformation.M41 = (adjustedOriginX * transformation.M11) + position.X;
                transformation.M42 = (adjustedOriginY * transformation.M22) + position.Y;
            }
            else
            {
                cos = MathF.Cos(rotation);
                sin = MathF.Sin(rotation);
                transformation.M11 = (flippedHorz ? -scaleVal.X : scaleVal.X) * cos;
                transformation.M12 = (flippedHorz ? -scaleVal.X : scaleVal.X) * sin;
                transformation.M21 = (flippedVert ? -scaleVal.Y : scaleVal.Y) * (-sin);
                transformation.M22 = (flippedVert ? -scaleVal.Y : scaleVal.Y) * cos;

                transformation.M41 = ((adjustedOriginX * transformation.M11) + adjustedOriginY * transformation.M21)
                                     + position.X;

                transformation.M42 = ((adjustedOriginX * transformation.M12) + adjustedOriginY * transformation.M22)
                                     + position.Y;
            }

            var offsetX = 0f;
            var offsetY = 0f;
            var firstGlyphOfLine = true;

            TextColor styleColor = color;

            bool styleRandom = false,
                styleBold = style.HasFlag(FontStyle.Bold),
                styleItalic = style.HasFlag(FontStyle.Italic),
                styleUnderline = style.HasFlag(FontStyle.Underline),
                styleStrikethrough = style.HasFlag(FontStyle.StrikeThrough),
                dropShadow = style.HasFlag(FontStyle.DropShadow);

            var blendFactor = sb.GraphicsDevice.BlendFactor;
            sb.GraphicsDevice.BlendFactor = Color.White * opacity;

            Vector2 underlineStart = Vector2.Zero;
            Vector2 strikeStart = Vector2.Zero;
            //Vector2 underlineEnd = Vector2.Zero;
            
            bool previousUnderline = false;
            bool previousStrike = false;
            var p = new Vector2(offsetX, offsetY);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '\r') continue;

                if (c == '\n')
                {
                    offsetX = 0.0f;
                    offsetY += LineSpacing;

                    firstGlyphOfLine = true;

                    styleRandom = false;
                    styleBold = false;
                    styleStrikethrough = false;
                    styleUnderline = false;
                    styleItalic = false;
                    styleColor = color;
                }
                else if (c == '\x00A7')
                {
                    // Formatting

                    // Get next character
                    if (i + 1 >= text.Length) continue;

                    i++;
                    var formatChar = text[i];

                    if (formatChar == 'k')
                    {
                        styleRandom = true;
                    }
                    else if (formatChar == 'l')
                    {
                        styleBold = true;
                    }
                    else if (formatChar == 'm')
                    {
                        styleStrikethrough = true;
                    }
                    else if (formatChar == 'n')
                    {
                        styleUnderline = true;
                    }
                    else if (formatChar == 'o')
                    {
                        styleItalic = true;
                    }
                    else if (formatChar == 'r')
                    {
                        styleRandom = false;
                        styleBold = false;
                        styleStrikethrough = false;
                        styleUnderline = false;
                        styleItalic = false;
                        styleColor = color;
                    }
                    else if (_colorChars.Contains(formatChar))
                    {
                        styleColor = TextColor.GetColor(formatChar);
                    }
                }
                else
                {
                    var glyph = GetGlyphOrDefault(c);

                    if (firstGlyphOfLine)
                    {
                        firstGlyphOfLine = false;
                    }

                    p.X = offsetX;
                    p.Y = offsetY;
                    
                    var width = glyph.Width + (styleBold ? 1f : 0f) + CharacterSpacing;
                    var height = glyph.Height;// + (styleBold ? 1f : 0f) + CharacterSpacing;
                    if (glyph.Texture != null)
                    {
                        var localRotation = rotation;
                        var localScale = glyph.Scale * scaleVal * Scale;
                        var localForegroundColor = styleColor.ForegroundColor.ToXna() * opacity;
                        var localBackgroundColor = styleColor.BackgroundColor.ToXna() * opacity;

                        if (styleRandom)
                        {
                            c = (char)FastRandom.Instance.Next(64, 126);
                            var g = GetGlyphOrDefault(c);

                            if (g?.Texture != null)
                            {
                                //localScale.X *= (1f / glyph.Width) * g.Width;
                                glyph = g;
                                
                            }
                        }

                        if (styleItalic)
                        {
                            localRotation += 0.15f;
                        }
                        
                        if (dropShadow)
                        {
                            var shadowP = p + Vector2.One;

                            if (styleBold)
                            {
                                var boldShadowP = Vector2.Transform(shadowP + Vector2.UnitX, transformation);

                                sb.Draw(
                                    glyph.Texture, boldShadowP, localBackgroundColor, localRotation,
                                    originVal, localScale, effects, layerDepth);
                            }

                            shadowP = Vector2.Transform(shadowP, transformation);

                            sb.Draw(
                                glyph.Texture, shadowP, localBackgroundColor, localRotation, originVal,
                                localScale, effects, layerDepth);
                        }

                        if (styleBold)
                        {
                            var boldP = Vector2.Transform(p + Vector2.UnitX, transformation);

                            sb.Draw(
                                glyph.Texture, boldP, localForegroundColor, localRotation, originVal,
                                localScale, effects, layerDepth);
                        }

                        p = Vector2.Transform(p, transformation);

                        sb.Draw(
                            glyph.Texture, p, localForegroundColor, localRotation, originVal,
                            localScale, effects, layerDepth);
                    }
                    
                    //Underline
                    if (styleUnderline && !previousUnderline)
                    {
                        //Start
                        underlineStart = Vector2.Transform(p + new Vector2(0, height), transformation);
                        previousUnderline = true;
                    }
                    else if (!styleUnderline && previousUnderline)
                    {
                        //End
                        //underlineEnd = new Vector2(underlineStart.X + width, underlineStart.Y);
                        previousUnderline = false;
                        
                        sb.DrawLine(2, underlineStart,new Vector2(underlineStart.X + width, underlineStart.Y), styleColor.ForegroundColor.ToXna() * opacity, glyph.Scale * scaleVal * Scale, layerDepth);
                    }
                    
                    //Strikethrough
                    if (styleStrikethrough && !previousStrike)
                    {
                        //Start
                        strikeStart = Vector2.Transform(p + new Vector2(0, (height / 2)), transformation);
                        styleStrikethrough = true;
                    }
                    else if (!styleStrikethrough && previousStrike)
                    {
                        //End
                        styleStrikethrough = false;
                        
                        sb.DrawLine(2, strikeStart, new Vector2(strikeStart.X + width, strikeStart.Y), styleColor.ForegroundColor.ToXna() * opacity, glyph.Scale * scaleVal * Scale, layerDepth);
                    }

                    offsetX += width;
                }
            }

            sb.GraphicsDevice.BlendFactor = blendFactor;
        }

        public IFontGlyph GetGlyphOrDefault(char character)
        {
            var index = character;

            if (!Glyphs.ContainsKey(character))
                return DefaultGlyph;

            return Glyphs[index];
        }

        private void LoadGlyphs(GraphicsDevice graphics, BitmapFontSource[] sources)
        {
            if (_isInitialised) return;
            
            Dictionary<char, IFontGlyph> glyphs = new Dictionary<char, IFontGlyph>();
            var asciiSource = sources.LastOrDefault(x => x.IsAscii && x.Image != null);
            if (asciiSource != null)
            {
                var previous = AsciiTexture;
                previous?.Dispose();
                
                AsciiTexture = TextureUtils.BitmapToTexture2D(this, graphics, asciiSource.Image);
                LoadGlyphs(ref glyphs, asciiSource.Image, AsciiTexture, asciiSource.Characters.Chunk(16).Select(c => new string(c)).ToArray(), false);
            }

            var unicodeSources = sources.Where(x => !x.IsAscii).ToArray();
            
            UnicodeTextures = new Texture2D[unicodeSources.Length];
            var i = 0;

            foreach (var source in unicodeSources)
            {
                var texture = TextureUtils.BitmapToTexture2D(this, graphics, source.Image);
                UnicodeTextures[i] = texture;

                LoadGlyphs(ref glyphs, source.Image, texture, source.Characters.Chunk(16).Select(c => new string(c)).ToArray(), true);
            }

            Glyphs = glyphs;
            DefaultGlyph = new Glyph('\x0000', AsciiTexture.Slice(0, 0, 0, 0), 8, 0, 1f);
            _isInitialised = true;
        }

        private void LoadGlyphs(ref Dictionary<char, IFontGlyph> glyphs, Image<Rgba32> bitmap, Texture2D texture, string[] data, bool isUnicode)
        {
            if (bitmap == null)
                return;
            
            if (_isInitialised) return;

            if (bitmap.DangerousTryGetSinglePixelMemory(out var mem))
            {
                var rgba = mem.Span;
                
                var textureWidth = bitmap.Width;
                var textureHeight = bitmap.Height;

                var cellHeight = (textureHeight / GridHeight);
                //var cellWidth = (textureWidth / GridWidth);

                for (int line = 0; line < data.Length; line++)
                {
                    var lineCharacters = data[line];

                    var cellWidth = (textureWidth / lineCharacters.Length);

                    for (int i = 0; i < lineCharacters.Length; i++)
                    {
                        var character = lineCharacters[i];

                        if (glyphs.ContainsKey(character))
                            continue;

                        int col = i;

                        // Scan the grid cell by pixel column, to determine the
                        // width of the characters.
                        int width = 0;
                        int height = 0;

                        bool columnIsEmpty = true;

                        for (var x = cellWidth - 1; x >= 0; x--)
                        {
                            columnIsEmpty = true;

                            for (var y = cellHeight - 1; y >= 0; y--)
                            {
                                var index = (textureWidth * (line * cellHeight + y)) + (col * cellWidth + x);

                                if (index < 0 || index >= rgba.Length)
                                    continue;

                                if (rgba[index].A != 0)
                                {
                                    columnIsEmpty = false;

                                    if (y > height)
                                        height = y;
                                }
                            }
                            
                            width = x;

                            if (!columnIsEmpty)
                            {
                                break;
                            }
                        }
                        
                        bool rowIsEmpty = true;
                        for (var y = cellHeight - 1; y >= 0; y--)
                        {
                            rowIsEmpty = true;

                            for (var x = cellWidth - 1; x >= 0; x--)
                            {
                                var index = (textureWidth * (line * cellHeight + y)) + (col * cellWidth + x);

                                if (index < 0 || index >= rgba.Length)
                                    continue;

                                if (rgba[index].A != 0)
                                {
                                    rowIsEmpty = false;
                                }
                            }
                            
                            height = y;

                            if (!rowIsEmpty)
                            {
                                break;
                            }
                        }

                        const float multiplier =  8.0f;

                        var charWidth = (0.5f + (width * (multiplier / cellWidth)) + 1f);
                        var charHeight = (0.5f + (height * (multiplier / cellHeight)) + 1f);

                        ++width;
                        ++height;

                        var bounds = new Rectangle(col * cellWidth, line * cellHeight, width, height);
                        var textureSlice = texture.Slice(bounds);

                        if (character == ' ')
                        {
                            charWidth = 4;
                        }

                        var glyph = new Glyph(
                            character, textureSlice, charWidth, charHeight,
                            /*isUnicode ? (charWidth / bounds.Width) : */isUnicode ? charWidth / width : 1f);

                        //Debug.WriteLine($"BitmapFont Glyph Loaded: {glyph}");

                        glyphs[character] = glyph;
                    }
                }
            }
        }

        public struct Glyph : IFontGlyph
        {
            public char Character { get; }
            public ITexture2D Texture { get; }
            public float Width { get; }
            public float Height { get; }
            public float Scale { get; }

            internal Glyph(char character, ITexture2D texture, float width, float height, float scale)
            {
                Character = character;
                Texture = texture;
                Width = width;
                Height = height;
                Scale = scale;
            }

            public override string ToString()
            {
                return $"CharacterIndex={Character}, Glyph={Texture}, Width={Width}, Height={Height}, Scale={Scale}";
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            AsciiTexture?.Dispose();
            AsciiTexture = null;

            var unicodeTextures = UnicodeTextures;

            if (unicodeTextures != null)
            {
                foreach(var texture in unicodeTextures)
                    texture?.Dispose();
            }

            UnicodeTextures = null;
        }
    }
}