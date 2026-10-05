// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

internal static class ProductIdentity
{
    internal const string Name = "Seele's SiMonitor";
    internal const string RepositoryUrl = "https://github.com/OrbisElegy/SiMonitor";
    internal const string CopyrightNotice = """
        Seele's SiMonitor - A patient monitor simulation software
        Copyright (C) 2026  Jason Zou (Aka. Orbis Elegy)

        This program is free software: you can redistribute it and/or modify
        it under the terms of the GNU Affero General Public License as published by
        the Free Software Foundation, either version 3 of the License, or
        any later version.

        This program is distributed in the hope that it will be useful,
        but WITHOUT ANY WARRANTY; without even the implied warranty of
        MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
        GNU Affero General Public License for more details.

        You should have received a copy of the GNU Affero General Public License
        along with this program. If not, see <https://www.gnu.org/licenses/>.
        """;
    internal const string Version = "V0.5";
#if SIMONITOR_RELEASE
    internal static bool DevelopmentFeatures => false;
#else
    internal static bool DevelopmentFeatures => true;
#endif
    // Development builds bind the localized shell.developmentTitle instead.
    internal const string WindowTitle = Name + " · " + Version + " Standalone";
}
