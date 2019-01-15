﻿using System;
using System.Collections.Generic;
using System.IO;
using NewLife.Data;
using NewLife.Threading;

namespace NewLife.Messaging
{
    /// <summary>数据包编码器</summary>
    public class PacketCodec
    {
        #region 属性
        /// <summary>缓存流</summary>
        public MemoryStream Stream { get; set; }

        /// <summary>获取长度的委托</summary>
        public Func<Packet, Int32> GetLength { get; set; }

        /// <summary>最后一次接收</summary>
        public DateTime Last { get; set; }

        /// <summary>缓存有效期。超过该时间后仍未匹配数据包的缓存数据将被抛弃</summary>
        public Int32 Expire { get; set; } = 5_000;

        /// <summary>最大缓存待处理数据。默认0无限制</summary>
        public Int32 MaxCache { get; set; }
        #endregion

        /// <summary>分析数据流，得到一帧数据</summary>
        /// <param name="pk">待分析数据包</param>
        /// <returns></returns>
        public virtual IList<Packet> Parse(Packet pk)
        {
            var ms = Stream;
            var nodata = ms == null || ms.Position < 0 || ms.Position >= ms.Length;

            var list = new List<Packet>();
            // 内部缓存没有数据，直接判断输入数据流是否刚好一帧数据，快速处理，绝大多数是这种场景
            if (nodata)
            {
                if (pk == null) return list.ToArray();

                var idx = 0;
                while (idx < pk.Total)
                {
                    // 切出来一片，计算长度
                    var pk2 = pk.Slice(idx);
                    var len = GetLength(pk2);
                    if (len <= 0 || len > pk2.Total) break;

                    // 根据计算得到的长度，重新设置数据片正确长度
                    pk2.Set(pk2.Data, pk2.Offset, len);
                    list.Add(pk2);
                    idx += len;
                }
                // 如果没有剩余，可以返回
                if (idx == pk.Total) return list.ToArray();

                // 剩下的
                pk = pk.Slice(idx);
            }

            // 加锁，避免多线程冲突
            lock (this)
            {
                CheckCache();

                // 合并数据到最后面
                if (pk != null && pk.Total > 0)
                {
                    var p = ms.Position;
                    ms.Position = ms.Length;
                    pk.WriteTo(ms);
                    ms.Position = p;
                }

                // 尝试解包
                while (ms.Position < ms.Length)
                {
                    var pk2 = new Packet(ms);
                    var len = GetLength(pk2);
                    if (len <= 0 || len > pk2.Total) break;

                    // 根据计算得到的长度，重新设置数据片正确长度
                    pk2.Set(pk2.Data, pk2.Offset, len);
                    list.Add(pk2);

                    ms.Seek(len, SeekOrigin.Current);
                }

                // 如果读完了数据，需要重置缓冲区
                if (ms.Position >= ms.Length)
                {
                    ms.SetLength(0);
                    ms.Position = 0;
                }

                return list;
            }
        }

        /// <summary>检查缓存</summary>
        protected virtual void CheckCache()
        {
            var ms = Stream;
            if (ms == null) Stream = ms = new MemoryStream();

            // 超过该时间后按废弃数据处理
            var now = TimerX.Now;
            if (ms.Length > ms.Position && Last.AddMilliseconds(Expire) < now && (MaxCache <= 0 || MaxCache <= ms.Length))
            {
                ms.SetLength(0);
                ms.Position = 0;
            }
            Last = now;
        }
    }
}