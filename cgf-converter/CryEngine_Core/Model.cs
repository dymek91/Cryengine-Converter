﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CgfConverter.CryEngine_Core;
using System.Linq.Expressions;
using System.Reflection;

namespace CgfConverter.CryEngine_Core
{

    /// <summary>
    /// CryEngine cgf/cga/skin file handler
    /// 
    /// Structure:
    ///   HEADER        <- Provides information about the format of the file
    ///   CHUNKHEADER[] <- Provides information about locations of CHUNKs
    ///   CHUNK[]
    /// </summary>
    public class Model
    {
        #region Public Properties

        /// <summary>
        /// The Root of the loaded object
        /// </summary>
        public ChunkNode RootNode { get; internal set; }

        /// <summary>
        /// Collection of all loaded Chunks
        /// </summary>
        public List<ChunkHeader> ChunkHeaders { get; internal set; }

        /// <summary>
        /// All the node chunks in this Model
        /// </summary>
        public List<ChunkNode> ChunkNodes { get; set; }
        
        /// <summary>
        /// Lookup Table for Chunks, indexed by ChunkID
        /// </summary>
        public Dictionary<int, Chunk> ChunkMap { get; internal set; }

        /// <summary>
        /// The name of the currently processed file
        /// </summary>
        public String FileName { get; internal set; }
        
        /// <summary>
        /// The File Signature - CryTek for 3.5 and lower. CrCh for 3.6 and higher
        /// </summary>
        public String FileSignature { get; internal set; }
        
        /// <summary>
        /// The type of file (geometry or animation)
        /// </summary>
        public FileTypeEnum FileType { get; internal set; }
        
        /// <summary>
        /// The version of the file
        /// </summary>
        public FileVersionEnum FileVersion { get; internal set; }
        
        /// <summary>
        /// Position of the Chunk Header table
        /// </summary>
        public Int32 ChunkTableOffset { get; internal set; }

        /// <summary>
        /// Contains all the information about bones and skinning them.  This a reference to the Cryengine object, since multiple Models can exist for a single object).
        /// </summary>
        public SkinningInfo SkinningInfo { get; set; }

        /// <summary>
        /// The Bones in the model.  The CompiledBones chunk will have a unique RootBone.
        /// </summary>
        public ChunkCompiledBones Bones { get; internal set; }

        public UInt32 NumChunks { get; internal set; }

        private Dictionary<int, CryEngine_Core.ChunkNode> _nodeMap { get; set; }
        /// <summary>
        /// Node map for this model only.
        /// </summary>

        public Dictionary<int, ChunkNode> NodeMap      // This isn't right.  Nodes can have duplicate names.
        {
            get
            {
                if (this._nodeMap == null)
                {
                    this._nodeMap = new Dictionary<int, ChunkNode>() { };
                    ChunkNode rootNode = null;
                    //Utils.Log(LogLevelEnum.Info, "Mapping Model Nodes");
                    this.RootNode = rootNode = (rootNode ?? this.RootNode);  // Each model will have it's own rootnode.
                    foreach (CryEngine_Core.ChunkNode node in this.ChunkMap.Values.Where(c => c.ChunkType == ChunkTypeEnum.Node).Select(c => c as ChunkNode))
                    {
                        // Preserve existing parents
                        if (this._nodeMap.ContainsKey(node.ID))
                        {
                            ChunkNode parentNode = this._nodeMap[node.ID].ParentNode;

                            if (parentNode != null)
                                parentNode = this._nodeMap[parentNode.ID];

                            node.ParentNode = parentNode;
                        }

                        this._nodeMap[node.ID] = node;
                    }
                }
                return this._nodeMap;
            }
        }

        #endregion

        #region Private Fields

        public List<ChunkHeader> _chunks = new List<ChunkHeader> { };

        #endregion

        #region Calculated Properties

        public Int32 NodeCount { get { return this.ChunkMap.Values.Where(c => c.ChunkType == ChunkTypeEnum.Node).Count(); } }

        public Int32 BoneCount { get { return this.ChunkMap.Values.Where(c => c.ChunkType == ChunkTypeEnum.CompiledBones).Count(); } }

        #endregion

        #region Constructor/s

        public Model()
        {
            this.ChunkMap = new Dictionary<int, CryEngine_Core.Chunk> { };
            this.ChunkHeaders = new List<ChunkHeader> { };
            this.SkinningInfo = new SkinningInfo();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Load the specified file as a Model, and return it
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static Model FromFile(String fileName)
        {
            Model buffer = new Model();
            buffer.Load(fileName);
            return buffer;
        }

        /// <summary>
        /// Load a cgf/cga/skin file
        /// </summary>
        /// <param name="fileName"></param>
        public void Load(String fileName)
        {
            FileInfo inputFile = new FileInfo(fileName);

            Console.Title = String.Format("Processing {0}...", inputFile.Name);

            this.FileName = inputFile.Name;

            if (!inputFile.Exists)
                throw new FileNotFoundException();

            // Open the file for reading.
            BinaryReader reader = new BinaryReader(File.Open(fileName, FileMode.Open));
            // Get the header.  This isn't essential for .cgam files, but we need this info to find the version and offset to the chunk table
            this.Read_FileHeader(reader);
            this.Read_ChunkHeaders(reader);
            this.Read_Chunks(reader);
        }


        /// <summary>
        /// Output File Header to console for testing
        /// </summary>
        public void WriteFileHeader()
        {
            Utils.Log(LogLevelEnum.Debug, "*** HEADER ***");
            Utils.Log(LogLevelEnum.Debug, "    Header Filesignature: {0}", this.FileSignature);
            Utils.Log(LogLevelEnum.Debug, "    FileType:            {0:X}", this.FileType);
            Utils.Log(LogLevelEnum.Debug, "    ChunkVersion:        {0:X}", this.FileVersion);
            Utils.Log(LogLevelEnum.Debug, "    ChunkTableOffset:    {0:X}", this.ChunkTableOffset);
            Utils.Log(LogLevelEnum.Debug, "    NumChunks:           {0:X}", this.NumChunks);

            Utils.Log(LogLevelEnum.Debug, "*** END HEADER ***");
            return;
        }

        /// <summary>
        /// Output Chunk Table to console for testing
        /// </summary>
        public void WriteChunkTable()
        {
            Utils.Log(LogLevelEnum.Debug, "*** Chunk Header Table***");
            Utils.Log(LogLevelEnum.Debug, "Chunk Type              Version   ID        Size      Offset    ");
            foreach (ChunkHeader chkHdr in this._chunks)
            {
                Utils.Log(LogLevelEnum.Debug, "{0,-24:X}{1,-10:X}{2,-10:X}{3,-10:X}{4,-10:X}", chkHdr.ChunkType.ToString(), chkHdr.Version, chkHdr.ID, chkHdr.Size, chkHdr.Offset);
            }
            Console.WriteLine("*** Chunk Header Table***");
            Console.WriteLine("Chunk Type              Version   ID        Size      Offset    ");
            foreach (ChunkHeader chkHdr in this._chunks)
            {
                Console.WriteLine("{0,-24:X}{1,-10:X}{2,-10:X}{3,-10:X}{4,-10:X}", chkHdr.ChunkType.ToString(), chkHdr.Version, chkHdr.ID, chkHdr.Size, chkHdr.Offset);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Read FileHeader from stream
        /// </summary>
        /// <param name="b"></param>
        private void Read_FileHeader(BinaryReader b)
        {
            #region Detect FileSignature v3.6+

            b.BaseStream.Seek(0, 0);
            this.FileSignature = b.ReadFString(4);

            if (this.FileSignature == "CrCh")
            {
                // Version 3.6 or later
                this.FileVersion = (FileVersionEnum)b.ReadUInt32();    // 0x746
                this.NumChunks = b.ReadUInt32();       // number of Chunks in the chunk table
                this.ChunkTableOffset = b.ReadInt32(); // location of the chunk table

                return;
            }

            #endregion

            #region Detect FileSignature v3.5-

            b.BaseStream.Seek(0, 0);
            this.FileSignature = b.ReadFString(8);

            if (this.FileSignature == "CryTek")
            {

                // Version 3.5 or earlier
                this.FileType = (FileTypeEnum)b.ReadUInt32();
                this.FileVersion = (FileVersionEnum)b.ReadUInt32();    // 0x744 0x745
                this.ChunkTableOffset = b.ReadInt32() + 4;
                this.NumChunks = b.ReadUInt32();       // number of Chunks in the chunk table

                return;
            }

            #endregion

            throw new NotSupportedException(String.Format("Unsupported FileSignature {0}", this.FileSignature));
        }

        /// <summary>
        /// Read HeaderTable from stream
        /// </summary>
        /// <typeparam name="TChunkHeader"></typeparam>
        /// <param name="b">BinaryReader of file being read</param>
        private void Read_ChunkHeaders(BinaryReader b)
        {
            // need to seek to the start of the table here.  foffset points to the start of the table
            b.BaseStream.Seek(this.ChunkTableOffset, SeekOrigin.Begin);

            for (Int32 i = 0; i < this.NumChunks; i++)
            {
                ChunkHeader header = Chunk.New<ChunkHeader>((UInt32)this.FileVersion);
                header.Read(b);
                this._chunks.Add(header);
            }
            //this.WriteChunkTable();
        }

        /// <summary>
        /// Reads all the chunks in the Cryengine file.
        /// </summary>
        /// <param name="reader">BinaryReader for the Cryengine file.</param>
        private void Read_Chunks(BinaryReader reader)
        {
            foreach (ChunkHeader chkHdr in this._chunks)
            {
                this.ChunkMap[chkHdr.ID] = Chunk.New(chkHdr.ChunkType, chkHdr.Version);
                this.ChunkMap[chkHdr.ID].Load(this, chkHdr);
                this.ChunkMap[chkHdr.ID].Read(reader);

                // Ensure we read to end of structure
                this.ChunkMap[chkHdr.ID].SkipBytes(reader);

                // TODO: Change this to detect node with ~0 (0xFFFFFFFF) parent ID
                // Assume first node read in Model[0] is root node.  This may be bad if they aren't in order!
                if (chkHdr.ChunkType == ChunkTypeEnum.Node && this.RootNode == null)
                {
                    this.RootNode = this.ChunkMap[chkHdr.ID] as ChunkNode;
                }

                // Add Bones to the model.  We are assuming there is only one CompiledBones chunk per file.
                if (chkHdr.ChunkType == ChunkTypeEnum.CompiledBones || chkHdr.ChunkType == ChunkTypeEnum.CompiledBonesSC)
                {
                    this.Bones = this.ChunkMap[chkHdr.ID] as ChunkCompiledBones;
                    SkinningInfo.HasSkinningInfo = true;
                }
            }
        }

        #endregion
    }
}
