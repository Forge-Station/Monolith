#nullable disable
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
// Modified for Wolfgate on 2026-11-01: moved into the game's client and adapted to its sandbox.



using System;
using System.IO;

namespace Content.Client._WF.DoomArcade.Engine
{
    public sealed class SaveSlots
    {
        private readonly GameOptions options;
        private static readonly int slotCount = 6;
        private static readonly int descriptionSize = 24;

        private string[] slots;

        public SaveSlots(GameOptions options)
        {
            this.options = options;
        }

        private void ReadSlots()
        {
            var saves = options.Saves;
            slots = new string[slotCount];

            var directory = ConfigUtilities.GetExeDirectory();
            var buffer = new byte[descriptionSize];
            for (var i = 0; i < slots.Length; i++)
            {
                var path = Path.Combine(directory, "doomsav" + i + ".dsg");
                if (saves.Exists(path))
                {
                    {
                        Array.Copy(saves.ReadAllBytes(path), buffer, buffer.Length);
                        slots[i] = DoomInterop.ToString(buffer, 0, buffer.Length);
                    }
                }
            }
        }

        public string this[int number]
        {
            get
            {
                if (slots == null)
                {
                    ReadSlots();
                }

                return slots[number];
            }

            set
            {
                slots[number] = value;
            }
        }

        public int Count => slots.Length;
    }
}
