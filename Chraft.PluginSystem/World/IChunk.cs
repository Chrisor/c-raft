﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.PluginSystem.Blocks;
using Chraft.Utilities;

namespace Chraft.PluginSystem
{
    public interface IChunk
    {

        IClient[] GetClients();
        IEntityBase[] GetEntities();
        bool LightToRecalculate { get; set; }
        UniversalCoords Coords { get; set; }
        void SetLightToRecalculate();
        IStructBlock GetBlock(UniversalCoords coords);
        IStructBlock GetBlock(int blockX, int blockY, int blockZ);
        byte GetBlockLight(UniversalCoords coords);
        byte GetBlockLight(int blockX, int blockY, int blockZ);
        byte GetSkyLight(UniversalCoords coords);
        byte GetSkyLight(int blockX, int blockY, int blockZ);
        byte GetData(UniversalCoords coords);
        byte GetData(int blockX, int blockY, int blockZ);
        byte GetDualLight(UniversalCoords coords);
        byte GetDualLight(int blockX, int blockY, int blockZ);
        byte GetLuminance(UniversalCoords coords);
        byte GetLuminance(int blockX, int blockY, int blockZ);
        byte GetOpacity(UniversalCoords coords);
        byte GetOpacity(int blockX, int blockY, int blockZ);
        void SetAllBlocks(byte[] data);
        BlockData.Blocks GetType(UniversalCoords coords);
        BlockData.Blocks GetType(int blockX, int blockY, int blockZ);
        void SetType(UniversalCoords coords, BlockData.Blocks value, bool needsUpdate = true);
        void SetType(int blockX, int blockY, int blockZ, BlockData.Blocks value, bool needsUpdate = true);
        void SetBlockAndData(UniversalCoords coords, byte type, byte data, bool needsUpdate = true);
        void SetBlockAndData(int blockX, int blockY, int blockZ, byte type, byte data, bool needsUpdate = true);
        void SetData(UniversalCoords coords, byte value, bool needsUpdate = true);
        void SetData(int blockX, int blockY, int blockZ, byte value, bool needsUpdate = true);
        void SetDualLight(UniversalCoords coords, byte value);
        void SetDualLight(int blockX, int blockY, int blockZ, byte value);
        void SetBlockLight(UniversalCoords coords, byte value);
        void SetBlockLight(int blockX, int blockY, int blockZ, byte value);
        void SetSkyLight(UniversalCoords coords, byte value);
        void SetSkyLight(int blockX, int blockY, int blockZ, byte value);
        bool IsAir(UniversalCoords coords);
        void BlockNeedsUpdate(int blockX, int blockY, int blockZ);
        void Dispose();
        void RecalculateLight();
        void RecalculateHeight();
        void RecalculateHeight(UniversalCoords coords);
        void RecalculateHeight(int x, int z);
        void RecalculateSky();
        void RecalculateSky(int x, int z);
        void SpreadLightFromBlock(byte x, byte y, byte z, byte light, byte oldHeight);
        void SpreadSkyLightFromBlock(byte x, byte y, byte z, bool sourceBlock = false);
        void MarkToSave();
        bool IsAdjacentTo(UniversalCoords coords, byte block);
        bool IsNSEWTo(UniversalCoords coords, byte block);
    }
}
