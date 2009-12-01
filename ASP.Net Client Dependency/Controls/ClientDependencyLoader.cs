﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Web.UI;
using System.Web;
using System.Linq;
using ClientDependency.Core.Config;
using ClientDependency.Core.FileRegistration.Providers;

namespace ClientDependency.Core.Controls
{
	[ParseChildren(typeof(ClientDependencyPath), ChildrenAsProperties = true)]
	public class ClientDependencyLoader : Control
	{
		/// <summary>
		/// Constructor sets the defaults.
		/// </summary>
		public ClientDependencyLoader()
		{
			Paths = new ClientDependencyPathCollection();
            
            //by default the provider is the default provider 
            Provider = ClientDependencySettings.Instance.DefaultFileRegistrationProvider;
            
            //default debug mode is specified in config but can be overriden in control.
            this.IsDebugMode = ClientDependencySettings.Instance.IsDebugMode;

			//add this object to the context and validate the context type
			if (HttpContext.Current != null)
			{
				if (HttpContext.Current.Items.Contains(ContextKey))
					throw new InvalidOperationException("Only one ClientDependencyLoader may exist on a page");
				//The context needs to be a page
				Page page = HttpContext.Current.Handler as Page;
				if (page == null)
					throw new InvalidOperationException("ClientDependencyLoader only works with Page based handlers.");
				HttpContext.Current.Items[ContextKey] = this;
			}
			else
				throw new InvalidOperationException("ClientDependencyLoader requires an HttpContext");
		}

		private const string ContextKey = "ClientDependencyLoader";

		/// <summary>
		/// Singleton per request instance.
		/// </summary>
		/// <exception cref="NullReferenceException">
		/// If no ClientDependencyLoader control exists on the current page, an exception is thrown.
		/// </exception>
		public static ClientDependencyLoader Instance
		{
			get
			{
				if (!HttpContext.Current.Items.Contains(ContextKey))
					return null;
				return HttpContext.Current.Items[ContextKey] as ClientDependencyLoader;
			}
		}

		/// <summary>
		/// Tracks all dependencies and maintains a deduplicated list
		/// </summary>
		private List<ProviderDependencyList> m_Dependencies = new List<ProviderDependencyList>();
		/// <summary>
		/// Tracks all paths and maintains a deduplicated list
		/// </summary>
		private HashSet<IClientDependencyPath> m_Paths = new HashSet<IClientDependencyPath>();

		/// <summary>
		/// Need to set the container for each of the paths to support databinding.
		/// </summary>
		protected override void CreateChildControls()
		{
			base.CreateChildControls();
			foreach (ClientDependencyPath path in Paths)
			{
				path.Parent = this;
			}	
		}

		/// <summary>
		/// Need to bind all children paths.
		/// </summary>
		/// <param name="e"></param>
		protected override void OnDataBinding(EventArgs e)
		{
			base.OnDataBinding(e);
			foreach (ClientDependencyPath path in Paths)
			{
				path.DataBind();
			}				
		}

		/// <summary>
		/// TODO: Can we render everything after pre render somehow? if we do it on the render method
		/// than provider need to actual 'write' their own output and can't rely on scriptmanager or page
		/// to render dependencies.
		/// </summary>
		/// <param name="e"></param>
		protected override void OnPreRender(EventArgs e)
		{
			base.OnPreRender(e);

			m_Paths.UnionWith(Paths.Cast<IClientDependencyPath>());
			Provider.IsDebugMode = IsDebugMode;
			RegisterClientDependencies(Provider, this.Page, m_Paths);
			RenderDependencies();
		}

		private void RenderDependencies()
		{
			m_Dependencies.ForEach(x =>
				{
					x.Provider.RegisterDependencies(this.Page, x.Dependencies, m_Paths);
				});
		}

		[PersistenceMode(PersistenceMode.InnerProperty)]
		public ClientDependencyPathCollection Paths { get; private set; }

        public string ProviderName
        {
            get
            {                
                return Provider.Name;
            }
            set
            {
                this.Provider = ClientDependencySettings.Instance.FileRegistrationProviderCollection[value];
            }
        }
        //public ClientDependencyEmbedType EmbedType
        //{
        //    get
        //    {
        //        return m_EmbedType;
        //    }
        //    set
        //    {
        //        m_EmbedType = value;
        //        switch (m_EmbedType)
        //        {
        //            case ClientDependencyEmbedType.Header:
        //                Provider = ClientDependencySettings.Instance.ProviderCollection[PageHeaderProvider.DefaultName];
        //                break;
        //            case ClientDependencyEmbedType.ClientSideRegistration:
        //                Provider = ClientDependencySettings.Instance.ProviderCollection[ClientSideRegistrationProvider.DefaultName];
        //                break;
        //        }
        //    }
        //}
        //private ClientDependencyEmbedType m_EmbedType = ClientDependencyEmbedType.Header;

        public bool IsDebugMode { get; set; }
		public BaseFileRegistrationProvider Provider { get; set; }



		#region Static Helper methods

		/// <summary>
		/// Checks if a loader already exists, if it does, it returns it, otherwise it will
		/// create a new one in the control specified.
		/// isNew will be true if a loader was created, otherwise false if it already existed.
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="isNew"></param>
		/// <returns></returns>
		public static ClientDependencyLoader TryCreate(Control parent, out bool isNew)
		{
			if (ClientDependencyLoader.Instance == null)
			{
				ClientDependencyLoader loader = new ClientDependencyLoader();
				parent.Controls.Add(loader);
				isNew = true;
				return loader;
			}
			else
			{
				isNew = false;
				return ClientDependencyLoader.Instance;
			}

		}

        public static string GetCompositeUrl(IEnumerable<string> urls, ClientDependencyType type)
        {
            List<IClientDependencyFile> list = urls
                .Select(x => new BasicClientDependencyFile(type) {
                     FilePath = x                      
                }).Cast<IClientDependencyFile>()
                .ToList();
            return ClientDependencySettings.Instance
                .DefaultFileRegistrationProvider.ProcessCompositeList(list, type)[0];          
        }

		#endregion

		/// <summary>
		/// Registers a file dependency
		/// </summary>
		/// <param name="filePath"></param>
		/// <param name="type"></param>
		public ClientDependencyLoader RegisterDependency(string filePath, ClientDependencyType type)
		{
			RegisterDependency(filePath, "", type);
			return this;
		}

		/// <summary>
		/// Registers a file dependency 
		/// </summary>
		/// <param name="filePath"></param>
		/// <param name="pathNameAlias"></param>
		/// <param name="type"></param>
		public ClientDependencyLoader RegisterDependency(string filePath, string pathNameAlias, ClientDependencyType type)
		{
			ClientDependencyLoader.Instance.RegisterDependency(ClientDependencyInclude.DefaultPriority, filePath, pathNameAlias, type);
			return this;
		}
		
		/// <summary>
		/// Adds a path to the current loader
		/// </summary>
		/// <param name="pathNameAlias"></param>
		/// <param name="path"></param>
		public ClientDependencyLoader AddPath(string pathNameAlias, string path)
		{
			AddPath(new BasicClientDependencyPath() { Name = pathNameAlias, Path = path });
			return this;
		}

		/// <summary>
		/// Adds a path to the current loader
		/// </summary>
		/// <param name="pathNameAlias"></param>
		/// <param name="path"></param>
		public void AddPath(IClientDependencyPath path)
		{
			m_Paths.Add(path);
		}

		/// <summary>
		/// Dynamically registers a dependency into the loader at runtime.
		/// This is similar to ScriptManager.RegisterClientScriptInclude.
		/// Registers a file dependency with the default provider.
		/// </summary>
		/// <param name="file"></param>
		public void RegisterDependency(int priority, string filePath, string pathNameAlias, ClientDependencyType type)
		{
			IClientDependencyFile file = new BasicClientDependencyFile(type);
			file.Priority = priority;
			file.FilePath = filePath;
			file.PathNameAlias = pathNameAlias;
			RegisterClientDependencies(new ClientDependencyCollection() { file }, new List<IClientDependencyPath>()); //send an empty paths collection
		}

		/// <summary>
		/// Registers dependencies with the specified provider.
		/// </summary>
		/// <param name="provider"></param>
		/// <param name="dependencies"></param>
		/// <param name="paths"></param>
		/// <remarks>
		/// This is the top most overloaded method
		/// </remarks>
		public void RegisterClientDependencies(BaseFileRegistrationProvider provider, ClientDependencyCollection dependencies, IEnumerable<IClientDependencyPath> paths)
		{
			//find or create the ProviderDependencyList for the provider
			ProviderDependencyList currList = m_Dependencies
				.Where(x => x.Contains(provider))
				.DefaultIfEmpty(new ProviderDependencyList(provider))
				.SingleOrDefault();
			//add the dependencies that don't have a provider specified
			currList.AddDependencies(dependencies
				.Where(x => string.IsNullOrEmpty(x.ForceProvider)));
			//add the list if it is new
			if (!m_Dependencies.Contains(currList))
				m_Dependencies.Add(currList);

			//we need to look up all of the dependencies that have forced providers, 
			//check if we've got a provider list for it, create one if not and add the dependencies
			//to it.
			List<BaseFileRegistrationProvider> forceProviders = new List<BaseFileRegistrationProvider>();
			var allProviderNamesInList = dependencies
				.Select(x => x.ForceProvider)
				.Where(x => !string.IsNullOrEmpty(x))
				.Distinct();
			foreach (var provName in allProviderNamesInList)
			{
				if (ClientDependencySettings.Instance.FileRegistrationProviderCollection[provName] != null)
					forceProviders.Add(ClientDependencySettings.Instance.FileRegistrationProviderCollection[provName]);
			}
			foreach (var prov in forceProviders)
			{
				//find or create the ProviderDependencyList for the prov
				ProviderDependencyList forceList = m_Dependencies
					.Where(x => x.Contains(prov))
					.DefaultIfEmpty(new ProviderDependencyList(prov))
					.SingleOrDefault();
				//add the dependencies that don't have a force provider specified
				forceList.AddDependencies(dependencies
					.Where(x => x.ForceProvider == prov.Name));
				//add the list if it is new
				if (!m_Dependencies.Contains(forceList))
					m_Dependencies.Add(forceList);
			}
			
			//add the paths, ensure no dups
			m_Paths.UnionWith(paths);
		}

		public void RegisterClientDependencies(ClientDependencyCollection dependencies, IEnumerable<IClientDependencyPath> paths)
		{
			//RegisterClientDependencies(ClientDependencySettings.Instance.DefaultProvider, dependencies, paths);
			RegisterClientDependencies(Provider, dependencies, paths);
		}

		/// <summary>
		/// Registers dependencies
		/// </summary>
		/// <param name="control"></param>
		/// <param name="paths"></param>
		public void RegisterClientDependencies(Control control, ClientDependencyPathCollection paths)
		{
			//RegisterClientDependencies(ClientDependencySettings.Instance.DefaultProvider, control, paths.Cast<IClientDependencyPath>());
			RegisterClientDependencies(Provider, control, paths.Cast<IClientDependencyPath>());
		}

		/// <summary>
		/// Registers dependencies with the provider name specified
		/// </summary>
		/// <param name="providerName"></param>
		/// <param name="control"></param>
		/// <param name="paths"></param>
		public void RegisterClientDependencies(string providerName, Control control, IEnumerable<IClientDependencyPath> paths)
		{
			RegisterClientDependencies(ClientDependencySettings.Instance.FileRegistrationProviderCollection[providerName], control, paths);
		}

		/// <summary>
		/// Registers dependencies with the provider specified by T
		/// </summary>
		public void RegisterClientDependencies<T>(Control control, List<IClientDependencyPath> paths)
			where T : BaseFileRegistrationProvider
		{
			//need to find the provider with the type
			BaseFileRegistrationProvider found = null;
			foreach (BaseFileRegistrationProvider p in ClientDependencySettings.Instance.FileRegistrationProviderCollection)
			{
				if (p.GetType().Equals(typeof(T)))
				{
					found = p;
					break;
				}
			}
			if (found == null)
				throw new ArgumentException("Could not find the ClientDependencyProvider specified by T");

			RegisterClientDependencies(found, control, paths);
		}

		public void RegisterClientDependencies(BaseFileRegistrationProvider provider, Control control, IEnumerable<IClientDependencyPath> paths)
		{
			ClientDependencyCollection dependencies = FindDependencies(control);
			RegisterClientDependencies(provider, dependencies, paths);
		}

		/// <summary>
		/// Recursively find all dependencies of this control and it's entire child control heirarchy.
		/// </summary>
		/// <param name="control"></param>
		/// <returns></returns>
		private ClientDependencyCollection FindDependencies(Control control)
		{
			// find dependencies
			Type controlType = control.GetType();
			ClientDependencyCollection dependencies = new ClientDependencyCollection();
			foreach (Attribute attribute in Attribute.GetCustomAttributes(controlType))
			{
				if (attribute is ClientDependencyAttribute)
				{
					dependencies.Add((ClientDependencyAttribute)attribute);
				}
			}

			// add child dependencies
			Type iClientDependency = typeof(IClientDependencyFile);
			foreach (Control child in control.Controls)
			{
				if (iClientDependency.IsAssignableFrom(child.GetType()))
				{
					IClientDependencyFile include = (IClientDependencyFile)child;
					dependencies.Add(include);
				}
				else
				{
					//recurse and de-duplicate!
					dependencies.UnionWith(FindDependencies(child));
				}
			}

			return dependencies;
		}		

	}

	

	
}