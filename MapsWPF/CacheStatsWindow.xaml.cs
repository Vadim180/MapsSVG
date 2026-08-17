using System;
using System.Windows;

namespace MapsWPF
{
    public record CacheStats
    {
        public string DbPath { get; init; }
        public double FileSizeMb { get; init; }
        public DateTime LastModified { get; init; }
        public int TileCount { get; init; }
        public int FromRam { get; init; }
        public int FromSQLite { get; init; }
        public int FromNetwork { get; init; }
        public string Mode { get; init; }
        public bool UseMemoryCache { get; init; }
        public bool CacheOnIdleRead { get; init; }
        public bool BoostCacheEngine { get; init; }

        public override string ToString()
        {
            return $"Cache Stats:\n\n" +
                   $"DB Path: {DbPath}\n\n" +
                   $"File Size: {FileSizeMb:F2} MB\n" +
                   $"Last Modified: {LastModified}\n\n" +
                   $"Tiles in DB: {TileCount}\n\n" +
                   $"Statistics:\n" +
                   $"  - From RAM: {FromRam}\n" +
                   $"  - From SQLite: {FromSQLite}\n" +
                   $"  - From Network: {FromNetwork}\n\n" +
                   $"Mode: {Mode}\n" +
                   $"UseMemoryCache: {UseMemoryCache}\n" +
                   $"CacheOnIdleRead: {CacheOnIdleRead}\n" +
                   $"BoostCacheEngine: {BoostCacheEngine}";
        }
    }

    public partial class CacheStatsWindow : Window
    {
        public CacheStatsWindow()
        {
            InitializeComponent();
        }

        private CacheStats _stats;

        public CacheStats Stats
        {
            get => _stats;
            set
            {
                _stats = value;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            if (_stats == null)
                return;

            try
            {
                TextBlockDbPath.Text = _stats.DbPath ?? string.Empty;
                TextBlockFileSize.Text = $"{_stats.FileSizeMb:F2} MB";
                TextBlockLastModified.Text = _stats.LastModified.ToString();
                TextBlockTileCount.Text = _stats.TileCount.ToString();

                TextBlockFromRam.Text = _stats.FromRam.ToString();
                TextBlockFromSQLite.Text = _stats.FromSQLite.ToString();
                TextBlockFromNetwork.Text = _stats.FromNetwork.ToString();

                // Color-coding: prefer green for RAM, orange when SQLite used with network, red when network dominates
                if (_stats.FromNetwork == 0 && (_stats.FromRam > 0 || _stats.FromSQLite > 0))
                {
                    TextBlockFromRam.Foreground = System.Windows.Media.Brushes.Green;
                    TextBlockFromSQLite.Foreground = System.Windows.Media.Brushes.Green;
                    TextBlockFromNetwork.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (_stats.FromSQLite > 0 && _stats.FromNetwork > 0)
                {
                    TextBlockFromRam.Foreground = System.Windows.Media.Brushes.Orange;
                    TextBlockFromSQLite.Foreground = System.Windows.Media.Brushes.Orange;
                    TextBlockFromNetwork.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else if (_stats.FromNetwork > 0)
                {
                    TextBlockFromRam.Foreground = System.Windows.Media.Brushes.Red;
                    TextBlockFromSQLite.Foreground = System.Windows.Media.Brushes.Red;
                    TextBlockFromNetwork.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    var gray = System.Windows.Media.Brushes.Gray;
                    TextBlockFromRam.Foreground = gray;
                    TextBlockFromSQLite.Foreground = gray;
                    TextBlockFromNetwork.Foreground = gray;
                }

                TextBlockMode.Text = _stats.Mode;
                TextBlockUseMemoryCache.Text = _stats.UseMemoryCache.ToString();
                TextBlockCacheOnIdleRead.Text = _stats.CacheOnIdleRead.ToString();
                TextBlockBoostCacheEngine.Text = _stats.BoostCacheEngine.ToString();
            }
            catch { }
        }
    }
}