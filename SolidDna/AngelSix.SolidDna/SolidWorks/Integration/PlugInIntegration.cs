﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AngelSix.SolidDna
{
    /// <summary>
    /// Provides functions related to SolidDna plug-ins
    /// </summary>
    public static class PlugInIntegration
    {
        #region Private Members

        /// <summary>
        /// The AppDomain used to load and unload plug-ins
        /// </summary>
        private static AppDomain PlugInAppDomain;

        /// <summary>
        /// The cross-domain marshal to use for the plug-in app domain calls
        /// </summary>
        private static PlugInIntegrationMarshal mCrossDomain;

        #endregion

        #region Public Properties

        /// <summary>
        /// A list of assembly full names to resolve across domains, excluding anything else that may be fouind in <see cref="PlugInDetails"/>
        /// </summary>
        public static List<string> AssembliesToResolve { get; set; } = new List<string>();

        /// <summary>
        /// If true, attempts to resolve all assemblies
        /// </summary>
        public static bool ResolveAllAssemblies { get; set; }

        /// <summary>
        /// A list of all plug-ins that have been added to be loaded
        /// The key is the absolute file path, and the Type is the <see cref="SolidPlugIn"/> implementation type
        /// </summary>
        public static Dictionary<string, List<PlugInDetails>> PlugInDetails { get; private set; } = new Dictionary<string, List<PlugInDetails>>();

        /// <summary>
        /// The location of the SolidDna dll file and any plug-in's
        /// </summary>
        public static string PlugInFolder { get { return typeof(PlugInIntegration).CodeBaseNormalized(); } }

        /// <summary>
        /// The cross-domain marshal to use for the plug-in app domain calls
        /// </summary>
        public static PlugInIntegrationMarshal CrossDomain {  get { return mCrossDomain; } }

        #endregion

        #region Public Events

        /// <summary>
        /// Called when a SolidWorks callback is fired
        /// </summary>
        public static event Action<string> CallbackFired = (name) => { };

        #endregion

        #region Setup / Teardown

        /// <summary>
        /// Must be called to setup the PlugInIntegration application domain
        /// </summary>
        public static void Setup(string revisionNumber, int cookie)
        {
            // Make sure we resolve assemblies in this domain, as it seems to use this domain to resolve
            // assemblies not the appDomain when crossing boundaries
            AppDomain.CurrentDomain.AssemblyResolve += PlugInIntegrationMarshal.AppDomain_AssemblyResolve;

            PlugInAppDomain = AppDomain.CreateDomain("SolidDnaPlugInDomain", null, new AppDomainSetup
            {
                // Use plug-in folder for resolving plug-ins
                ApplicationBase = PlugInFolder,
            });

            // Make sure we load our own marshal
            AssembliesToResolve.Add(typeof(PlugInIntegrationMarshal).Assembly.FullName);

            // Run code on new app-domain to configure
            mCrossDomain = (PlugInIntegrationMarshal)PlugInAppDomain.CreateInstanceAndUnwrap(typeof(PlugInIntegrationMarshal).Assembly.FullName, typeof(PlugInIntegrationMarshal).FullName);
            mCrossDomain.SetupAppDomain(revisionNumber, cookie);
        }

        /// <summary>
        /// Cleans up the plug-in app domain so that the plug-in dll files can be edited after unloading
        /// </summary>
        public static void Teardown()
        {
            // Run code on new app-domain to tear down
            mCrossDomain.TeardownAppDomain();

            // Unload our domain
            AppDomain.Unload(PlugInAppDomain);
        }

        #endregion

        #region Connected to SolidWorks

        /// <summary>
        /// Called when the add-in has connected to SolidWorks
        /// </summary>
        public static void ConnectedToSolidWorks()
        {
            mCrossDomain.ConnectedToSolidWorks();
        }

        /// <summary>
        /// Called when the add-in has disconnected from SolidWorks
        /// </summary>
        public static void DisconnectedFromSolidWorks()
        {
            mCrossDomain.DisconnectedFromSolidWorks();
        }

        #endregion

        #region Add Plug-in

        /// <summary>
        /// Adds a plug-in based on it's <see cref="SolidPlugIn"/> implementation
        /// </summary>
        /// <typeparam name="T">The class that implements the <see cref="SolidPlugIn"/></typeparam>
        /// </param>
        public static void AddPlugIn<T>()
        {
            // Get the full path to the assembly
            var fullPath = typeof(T).Assembly.CodeBase.Replace(@"file:\", "").Replace(@"file:///", "");

            // Create list if one doesn't exist
            if (!PlugInDetails.ContainsKey(fullPath))
                PlugInDetails[fullPath] = new List<PlugInDetails>();

            // Add it
            PlugInDetails[fullPath].Add(new PlugInDetails
            {
                FullPath = fullPath,
                AssemblyFullName = AssemblyName.GetAssemblyName(fullPath).FullName,
                TypeFullName = typeof(T).FullName,
            });

            if (PlugInAppDomain != null && AppDomain.CurrentDomain != PlugInAppDomain)
                // Add it to the plug-in integration domain also
                mCrossDomain.AddPlugIn<T>();
        }

        /// <summary>
        /// Adds a plug-in based on it's <see cref="SolidPlugIn"/> implementation
        /// </summary>
        /// <typeparam name="fullPath">The absolute path to the plug-in dll</typeparam>
        public static void AddPlugIn(string fullPath)
        {
            if (PlugInAppDomain != null && AppDomain.CurrentDomain != PlugInAppDomain)
                // Add it to the plug-in integration domain also
                mCrossDomain.AddPlugIn(fullPath);
            else
            {
                // We get here only if we are in the plug-in app domain

                // Create list if one doesn't exist
                if (!PlugInDetails.ContainsKey(fullPath))
                    PlugInDetails[fullPath] = new List<PlugInDetails>();

                List<PlugInDetails> plugins;

                plugins = GetPlugInDetails(fullPath);

                // Add any found plugins
                if (plugins?.Count > 0)
                    PlugInDetails[fullPath].AddRange(plugins);
            }
        }

        #endregion

        #region SolidWorks Callbacks

        /// <summary>
        /// Fires a <see cref="CallbackFired"/> event for the given name
        /// </summary>
        /// <param name="name">The name of the callback that was fired</param>
        public static void OnCallback(string name)
        {
            // Inform listeners
            CallbackFired(name);
        }

        #endregion

        #region Configure Plug Ins

        /// <summary>
        /// Discovers all SolidDna plug-ins
        /// </summary>
        /// <param name="loadAll">True to find all plug-ins in the same folder as the SolidDna dll</param>
        /// <returns></returns>
        public static List<SolidPlugIn> SolidDnaPlugIns(bool loadAll = true)
        {
            // Create new empry list
            var assemblies = new List<SolidPlugIn>();

            // Find all dll's in the same directory
            if (loadAll)
            { 
                // Clear old list
                PlugInDetails = new Dictionary<string, List<PlugInDetails>>();

                // Add new based on if found
                foreach (var path in Directory.GetFiles(PlugInFolder, "*.dll", SearchOption.TopDirectoryOnly))
                    GetPlugIns(path, (plugin) => assemblies.Add(plugin));
            }
            // Or load explicit ones
            else
            {
                // For each assembly
                foreach (var p in PlugInDetails)
                {
                    // And each plug-in inside it
                    foreach (var path in p.Value)
                    {
                        try
                        {
                            // If we are called in the main domain, cross-load
                            if (PlugInAppDomain != null)
                            {
                                // Create instance of the plug-in via cross-domain and cast back
                                var plugin = (dynamic)PlugInAppDomain.CreateInstanceAndUnwrap(
                                                        path.AssemblyFullName,
                                                        path.TypeFullName);

                                // If we got it, add it to the list
                                if (plugin != null)
                                    assemblies.Add(plugin);
                            }
                            else
                            {
                                GetPlugIns(path.FullPath, (plugin) => assemblies.Add(plugin));
                            }
                        }
                        catch
                        {

                        }
                    }
                }
            }

            return assemblies;
        }

        /// <summary>
        /// Loads the dll into the current app domain, and finds any <see cref="SolidPlugIn"/> implementations, calling onFound when it finds them
        /// </summary>
        /// <param name="pluginFullPath">The full path to the plug-in dll to load</param>
        /// <param name="onFound">Called when a <see cref="SolidPlugIn"/> is found</param>
        public static void GetPlugIns(string pluginFullPath, Action<SolidPlugIn> onFound)
        {
            // Load the assembly
            var ass = Assembly.LoadFile(pluginFullPath);

            // If we didn't succeed, ignore
            if (ass == null)
                return;

            var type = typeof(SolidPlugIn);

            // Now look through all types and see if any are of SolidPlugIn
            ass.GetTypes().Where(p => type.IsAssignableFrom(p) && p.IsClass && !p.IsAbstract).ToList().ForEach(p =>
            {
                // Create SolidDna plugin class instance
                var inter = Activator.CreateInstance(p) as SolidPlugIn;
                if (inter != null)
                    onFound(inter);
            });
        }

        /// <summary>
        /// Loads the assembly, finds all <see cref="SolidPlugIn"/> implementations and 
        /// creates a list of <see cref="PlugInDetails"/> for them
        /// </summary>
        /// <param name="fullPath">The assembly full path to load</param>
        /// <returns></returns>
        public static List<PlugInDetails> GetPlugInDetails(string fullPath)
        {
            var list = new List<PlugInDetails>();

            PlugInIntegration.GetPlugIns(fullPath, (plugin) => list.Add(new PlugInDetails
            {
                AssemblyFullName = plugin.GetType().AssemblyBaseNormalized(),
                FullPath = fullPath,
                TypeFullName = plugin.GetType().FullName
            }));

            return list;
        }

        /// <summary>
        /// Runs any initialization code reqiured on plug-ins
        /// </summary>
        public static void ConfigurePlugIns()
        {
            if (mCrossDomain != null)
                mCrossDomain.ConfigurePlugIns();
            else
            {
                // This is usually run for the ComRegister function

                // Try and find the title from the first plug-in found
                var plugins = PlugInIntegration.SolidDnaPlugIns(loadAll: true);
                if (plugins.Count > 0)
                {
                    AddInIntegration.SolidWorksAddInTitle = plugins.First().AddInTitle;
                    AddInIntegration.SolidWorksAddInDescription = plugins.First().AddInDescription;
                }
            }
        }

        #endregion
    }
}
