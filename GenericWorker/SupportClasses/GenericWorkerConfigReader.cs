using GenericWorker.Interfaces;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenericWorker.SupportClasses
{
    public class GenericWorkerConfigReader : IGenericWorkerConfigReader
    {
        #region Private Members
        private const string APP_SETTINGS_SHADOW_COPY_WORK_HANDLERS = "ShadowCopyWorkHandlers";
        private readonly AppSettingsReader _reader;
        #endregion

        #region Constructors
        public GenericWorkerConfigReader()
        {
            //defaults
            this.ShadowCopyWorkHandlers = true;

            _reader = new AppSettingsReader();
            LoadFromAppConfig();
        }
        #endregion

        #region Properties
        public bool ShadowCopyWorkHandlers { get; set; }
        #endregion

        #region Private Methods
        private void LoadFromAppConfig()
        {
            var shadowCopyWorkHandlers = ConfigurationManager.AppSettings[APP_SETTINGS_SHADOW_COPY_WORK_HANDLERS];
            if (shadowCopyWorkHandlers != null) this.ShadowCopyWorkHandlers = Convert.ToBoolean(shadowCopyWorkHandlers);
        }
        #endregion
    }
}
