using System;
using System.Collections.Generic;

namespace GMap.NET.Internals
{
    /// <summary>
    ///     matrix for tiles
    /// </summary>
    internal class TileMatrix : IDisposable
    {
        List<Dictionary<GPoint, Tile>> _levels = new List<Dictionary<GPoint, Tile>>(33);
        FastReaderWriterLock _lock = new FastReaderWriterLock();

        public TileMatrix()
        {
            for (int i = 0; i < _levels.Capacity; i++)
            {
                _levels.Add(new Dictionary<GPoint, Tile>(55, new GPointComparer()));
            }
        }

        public void ClearAllLevels()
        {
            _lock.AcquireWriterLock();
            try
            {
                foreach (var matrix in _levels)
                {
                    foreach (var t in matrix)
                    {
                        t.Value.Dispose();
                    }

                    matrix.Clear();
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        public void ClearLevel(int zoom)
        {
            _lock.AcquireWriterLock();
            try
            {
                if (zoom < _levels.Count)
                {
                    var l = _levels[zoom];

                    foreach (var t in l)
                    {
                        t.Value.Dispose();
                    }

                    l.Clear();
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        List<KeyValuePair<GPoint, Tile>> _tmp = new List<KeyValuePair<GPoint, Tile>>(44);
        HashSet<GPoint> _visibleTilesSet = new HashSet<GPoint>(new GPointComparer());

        /// <summary>
        /// Maximum number of tiles to keep in memory per zoom level beyond the visible area.
        /// Increasing this value reduces network requests when dragging back and forth,
        /// but uses more memory. Default: 100 tiles (~10MB at 100KB per tile).
        /// </summary>
        public int MaxTilesPerLevelBeyondVisible { get; set; } = 100;

        public void ClearLevelAndPointsNotIn(int zoom, List<DrawTile> list)
        {
            _lock.AcquireWriterLock();
            try
            {
                if (zoom < _levels.Count)
                {
                    var l = _levels[zoom];

                    // Only clear if we have significantly more tiles than visible + buffer
                    int maxAllowed = list.Count + MaxTilesPerLevelBeyondVisible;
                    if (l.Count <= maxAllowed)
                    {
                        // Don't clear - we're within the allowed buffer
                        return;
                    }

                    // Build HashSet for O(1) lookup instead of O(n) list.Exists
                    _visibleTilesSet.Clear();
                    foreach (var dt in list)
                    {
                        _visibleTilesSet.Add(dt.PosXY);
                    }

                    _tmp.Clear();

                    foreach (var t in l)
                    {
                        if (!_visibleTilesSet.Contains(t.Key))
                        {
                            _tmp.Add(t);
                        }
                    }

                    // Only remove excess tiles beyond our buffer
                    int tilesToRemove = l.Count - maxAllowed;
                    if (tilesToRemove > 0 && _tmp.Count > 0)
                    {
                        // Remove only the oldest tiles (first ones in _tmp)
                        int removeCount = Math.Min(tilesToRemove, _tmp.Count);
                        for (int i = 0; i < removeCount; i++)
                        {
                            var r = _tmp[i];
                            l.Remove(r.Key);
                            r.Value.Dispose();
                        }
                    }

                    _tmp.Clear();
                    _visibleTilesSet.Clear();
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        public void ClearLevelsBelove(int zoom)
        {
            _lock.AcquireWriterLock();
            try
            {
                if (zoom - 1 < _levels.Count)
                {
                    for (int i = zoom - 1; i >= 0; i--)
                    {
                        var l = _levels[i];

                        foreach (var t in l)
                        {
                            t.Value.Dispose();
                        }

                        l.Clear();
                    }
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        public void ClearLevelsAbove(int zoom)
        {
            _lock.AcquireWriterLock();
            try
            {
                if (zoom + 1 < _levels.Count)
                {
                    for (int i = zoom + 1; i < _levels.Count; i++)
                    {
                        var l = _levels[i];

                        foreach (var t in l)
                        {
                            t.Value.Dispose();
                        }

                        l.Clear();
                    }
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        public void EnterReadLock()
        {
            _lock.AcquireReaderLock();
        }

        public void LeaveReadLock()
        {
            _lock.ReleaseReaderLock();
        }

        public Tile GetTileWithNoLock(int zoom, GPoint p)
        {
            var ret = Tile.Empty;

            //if(zoom < Levels.Count)
            {
                _levels[zoom].TryGetValue(p, out ret);
            }

            return ret;
        }

        public Tile GetTileWithReadLock(int zoom, GPoint p)
        {
            var ret = Tile.Empty;

            _lock.AcquireReaderLock();
            try
            {
                ret = GetTileWithNoLock(zoom, p);
            }
            finally
            {
                _lock.ReleaseReaderLock();
            }

            return ret;
        }

        public void SetTile(Tile t)
        {
            _lock.AcquireWriterLock();
            try
            {
                if (t.Zoom < _levels.Count)
                {
                    _levels[t.Zoom][t.Pos] = t;
                }
            }
            finally
            {
                _lock.ReleaseWriterLock();
            }
        }

        #region IDisposable Members

        ~TileMatrix()
        {
            Dispose(false);
        }

        void Dispose(bool disposing)
        {
            if (_lock != null)
            {
                if (disposing)
                {
                    ClearAllLevels();
                }

                _levels.Clear();
                _levels = null;

                _tmp.Clear();
                _tmp = null;

                _lock.Dispose();
                _lock = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
