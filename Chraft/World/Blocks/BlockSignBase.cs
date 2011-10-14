﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Net.Packets;

namespace Chraft.World.Blocks
{
    public abstract class BlockSignBase : BlockBase
    {

        private object folderLock = new object();
        public void SaveText(UniversalCoords coords, Player player, string[] lines)
        {
            string folderPath = player.World.SignsFolder + "/x" + coords.ChunkX + "_z" + coords.ChunkZ;

            // Here it's possible that two or more different signs lead to the same folder, so we need to lock
            lock (folderLock)
            {
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);
            }

            string text = string.Join(String.Empty, lines.ToArray());
            /* Here it's "impossible" that we receive two updates of the same sign at the same time. We don't need to lock also
             * because we can't write a sign not loaded (so the read of the sign file can't happen at the same time of a write) */
            using (StreamWriter sw = new StreamWriter(String.Format("{0}/sign_{1}_{2}_{3}.txt", folderPath, coords.BlockX, coords.BlockY, coords.BlockZ)))
            {
                sw.WriteLine("{0}, {1}, {2} {3} {4}", text, player.DisplayName, coords.WorldX, coords.WorldY, coords.WorldZ);
            }


            player.GetCurrentChunk().SignsText.TryAdd(coords.BlockPackedCoords, text);
            player.Server.SendPacketToNearbyPlayers(player.World, coords, new UpdateSignPacket { X = coords.WorldX, Y = coords.WorldY, Z = coords.WorldZ, Lines = lines });
        }

        public void LoadSignsFromDisk(Chunk chunk, string signFolder)
        {
            string folderPath = signFolder + "/x" + chunk.Coords.ChunkX + "_z" + chunk.Coords.ChunkZ;

            if (Directory.Exists(folderPath))
            {
                string[] files = Directory.GetFiles(folderPath);

                foreach (string file in files)
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        string line = sr.ReadLine();
                        string[] parts = line.Split(',');

                        string[] coords = parts[2].TrimStart().Split(' ');

                        UniversalCoords signCoords = UniversalCoords.FromWorld(int.Parse(coords[0]),
                                                                               int.Parse(coords[1]),
                                                                               int.Parse(coords[2]));

                        string[] lines = new string[4];

                        int length = parts[0].Length;

                        for (int i = 0; i < 4; ++i, length -= 15)
                        {
                            int currentLength = length;
                            if (currentLength > 15)
                                currentLength = 15;

                            if (length > 0)
                                lines[i] = parts[0].Substring(i * 15, currentLength);
                            else
                                lines[i] = "";
                        }

                        chunk.SignsText.TryAdd(signCoords.BlockPackedCoords, parts[0]);
                    }
                }
            }
        }

        public override void Destroy(EntityBase entity, StructBlock block)
        {
            string folderPath = entity.World.SignsFolder + "/x" + block.Coords.ChunkX + "_z" + block.Coords.ChunkZ;

            if (Directory.Exists(folderPath))
            {
                string unused;
                File.Delete(String.Format("{0}/sign_{1}_{2}_{3}.txt", folderPath, block.Coords.BlockX,
                                          block.Coords.BlockY, block.Coords.BlockZ));
                block.Chunk.SignsText.TryRemove(block.Coords.BlockPackedCoords, out unused);
            }
            base.Destroy(entity, block);
        }
    }
}
