#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace DaleGhent.NINA.AstroPhysics {

    public class Utilities {

        public static DeepSkyObject FindDsoInfo(ISequenceContainer container) {
            DeepSkyObject target = null;
            ISequenceContainer acontainer = container;

            while (acontainer != null) {
                if (acontainer is IDeepSkyObjectContainer dsoContainer) {
                    target = dsoContainer.Target.DeepSkyObject;
                    break;
                }

                acontainer = acontainer.Parent;
            }

            return target;
        }

        public sealed class TemporaryFile : IDisposable {
            public TemporaryFile() :
              this(Path.GetTempPath()) { }

            public TemporaryFile(string directory) {
                Create(Path.Combine(directory, Path.GetRandomFileName()));
            }

            ~TemporaryFile() {
                Delete();
            }

            public void Dispose() {
                Delete();
                GC.SuppressFinalize(this);
            }

            public string FilePath { get; private set; }

            private void Create(string path) {
                FilePath = path;
                using (File.Create(FilePath)) { };
            }

            private void Delete() {
                if (FilePath == null) return;
                File.Delete(FilePath);
                FilePath = null;
            }
        }
    }
}