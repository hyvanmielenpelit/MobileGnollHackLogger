using Sentry;
using Sentry.Extensibility;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System;

namespace Overseer.Services
{
    public class AuthSentryEventProcessor : ISentryEventProcessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthSentryEventProcessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public SentryEvent? Process(SentryEvent @event)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext != null && httpContext.User?.Identity?.IsAuthenticated != true)
            {
                // Drop events from unauthenticated web requests (bots, probes)
                return null;
            }

            // Check if the event or its inner exceptions contain a transient HttpRequestException
            if (IsTransientApiOverloadException(@event.Exception))
            {
                return null;
            }

            return @event;
        }

        private bool IsTransientApiOverloadException(Exception? exception)
        {
            if (exception == null)
            {
                return false;
            }

            if (exception is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == HttpStatusCode.TooManyRequests || 
                    httpEx.StatusCode == HttpStatusCode.BadGateway || 
                    httpEx.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return true;
                }
            }

            if (exception is AggregateException aggEx)
            {
                foreach (var inner in aggEx.InnerExceptions)
                {
                    if (IsTransientApiOverloadException(inner))
                    {
                        return true;
                    }
                }
            }
            else if (exception.InnerException != null)
            {
                return IsTransientApiOverloadException(exception.InnerException);
            }

            return false;
        }
    }
}
