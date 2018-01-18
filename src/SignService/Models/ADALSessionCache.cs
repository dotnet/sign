using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace SignService.Models
{
    public class ADALSessionCache : TokenCache
    {
        ReaderWriterLockSlim SessionLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        string CacheId = string.Empty;
        IHttpContextAccessor httpContext = null;


        public ADALSessionCache(string userId, IHttpContextAccessor httpcontext)
        {
            // not object, we want the SUB
            CacheId = userId + "_TokenCache";
            httpContext = httpcontext;
            this.AfterAccess = AfterAccessNotification;
            this.BeforeAccess = BeforeAccessNotification;
            Load();
        }

        public void SaveUserStateValue(string state)
        {
            SessionLock.EnterWriteLock();

            httpContext.HttpContext.Session.SetString(CacheId + "_state", state);
            SessionLock.ExitWriteLock();
        }
        public string ReadUserStateValue()
        {
            string state = string.Empty;
            SessionLock.EnterReadLock();
            //this.Deserialize((byte[])HttpContext.Current.Session[CacheId]);
            state = httpContext.HttpContext.Session.GetString(CacheId + "_state");
            SessionLock.ExitReadLock();
            return state;
        }
        public void Load()
        {
            SessionLock.EnterReadLock();
            //this.Deserialize((byte[])HttpContext.Current.Session[CacheId]);
            this.Deserialize((byte[])httpContext.HttpContext.Session.Get(CacheId));
            SessionLock.ExitReadLock();
        }

        public void Persist()
        {
            SessionLock.EnterWriteLock();

            // Optimistically set HasStateChanged to false. We need to do it early to avoid losing changes made by a concurrent thread.
            this.HasStateChanged = false;

            // Reflect changes in the persistent store
            httpContext.HttpContext.Session.Set(CacheId, this.Serialize());
            SessionLock.ExitWriteLock();
        }

        // Empties the persistent store.
        public override void Clear()
        {
            base.Clear();
            httpContext.HttpContext.Session.Remove(CacheId);
        }

        // Triggered right before ADAL needs to access the cache.
        // Reload the cache from the persistent store in case it changed since the last access.
        void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            Load();
        }

        // Triggered right after ADAL accessed the cache.
        void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            // if the access operation resulted in a cache update
            if (this.HasStateChanged)
            {
                Persist();
            }
        }
    }
}
