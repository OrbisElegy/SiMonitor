// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Media;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorTrace
{
    private void ClearPreparedWaveforms()
    {
        // Submitted operations retain their own immutable snapshots. Releasing
        // these references cannot change a frame still owned by the renderer.
        _paths = new StreamGeometry?[0, 0];
        _contours = new Point[]?[0, 0];
        _ecgPoints = new Point[]?[0, 0];
        _contourPaths = new ContourCache?[0, 0];
        _ecgGeometrySnapshots = new EcgGeometrySnapshot?[0, 0];
        _currentContourSnapshots = [];
        _rowSweeps = [];
        _revision = ulong.MaxValue;
        _cycle = -1;
    }

    private void PrepareWaveformSnapshotRows(int rows)
    {
        if (_contourPaths.GetLength(0) != rows) { _contourPaths = new ContourCache?[rows, 2]; }
        if (_ecgGeometrySnapshots.GetLength(0) != rows)
        { _ecgGeometrySnapshots = new EcgGeometrySnapshot?[rows, 2]; }
        if (_currentContourSnapshots.Length != rows)
        { _currentContourSnapshots = new CurrentContourSnapshot?[rows]; }
    }

    private void ReleaseUnusedWaveformSnapshots(int row, int channel)
    {
        if (channel == 0)
        {
            _currentContourSnapshots[row] = null;
            _contourPaths[row, 0] = null;
            _contourPaths[row, 1] = null;
        }
        else
        {
            _ecgGeometrySnapshots[row, 0] = null;
            _ecgGeometrySnapshots[row, 1] = null;
        }
    }
}
