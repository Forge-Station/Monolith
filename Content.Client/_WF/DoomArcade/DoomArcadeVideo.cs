//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// Modified for Wolfgate on 2026-11-01: based on ManagedDoom's SilkVideo.

using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.DoomArcade.Engine.Video;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.DoomArcade;

/// <summary>Runs the software renderer and turns its column-major output into row-major pixels.</summary>
public sealed class DoomArcadeVideo : IVideo
{
    private readonly Renderer _renderer;
    private readonly uint[] _columns;

    public readonly Rgba32[] Pixels;

    public int Width => _renderer.Width;
    public int Height => _renderer.Height;

    public DoomArcadeVideo(Config config, GameContent content)
    {
        _renderer = new Renderer(config, content);
        _columns = new uint[Width * Height];
        Pixels = new Rgba32[Width * Height];
    }

    public void Render(Doom doom, Fixed frameFrac)
    {
        _renderer.Render(doom, _columns, frameFrac);

        var width = Width;
        var height = Height;
        for (var x = 0; x < width; x++)
        {
            var column = x * height;
            for (var y = 0; y < height; y++)
            {
                Pixels[y * width + x] = new Rgba32(_columns[column + y]);
            }
        }
    }

    public void InitializeWipe() => _renderer.InitializeWipe();

    public bool HasFocus() => true;

    public int MaxWindowSize => _renderer.MaxWindowSize;

    public int WindowSize
    {
        get => _renderer.WindowSize;
        set => _renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => _renderer.DisplayMessage;
        set => _renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel => _renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => _renderer.GammaCorrectionLevel;
        set => _renderer.GammaCorrectionLevel = value;
    }

    public int WipeBandCount => _renderer.WipeBandCount;
    public int WipeHeight => _renderer.WipeHeight;
}
