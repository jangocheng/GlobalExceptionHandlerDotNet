﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace GlobalExceptionHandler.WebApi
{	
	public class ExceptionHandlerConfiguration : IUnhandledFormatters
	{		
		private readonly IDictionary<Type, ExceptionConfig> _exceptionConfiguration = new Dictionary<Type, ExceptionConfig>();
		private Type[] _exceptionConfgurationTypesSortedByDepthDescending;		
		private Func<Exception, HttpContext, Task> _logger;

		internal Func<Exception, HttpContext, HandlerContext, Task> CustomFormatter { get; private set; } 
		internal Func<Exception, HttpContext, HandlerContext, Task> DefaultFormatter { get; private set; }
		internal IDictionary<Type, ExceptionConfig> ExceptionConfiguration => _exceptionConfiguration;

		public string ContentType { get; set; }
		public bool DebugMode { get; set; }

		public ExceptionHandlerConfiguration(Func<Exception, HttpContext, HandlerContext, Task> defaultFormatter) => DefaultFormatter = defaultFormatter;

		public IHasStatusCode ForException<T>() where T : Exception
		{
			var type = typeof(T);
			return new ExceptionRuleCreator(_exceptionConfiguration, type);
		}
		
		public void MessageFormatter(Func<Exception, string> formatter)
		{
			Task Formatter(Exception x, HttpContext y, HandlerContext b)
			{
				var s = formatter.Invoke(x);
				y.Response.WriteAsync(s);
				return Task.CompletedTask;
			}
			
			MessageFormatter(Formatter);
		}
		
		public void MessageFormatter(Func<Exception, HttpContext, Task> formatter)
		{
			Task Formatter(Exception x, HttpContext y, HandlerContext b)
			{
				formatter.Invoke(x, y);
				return Task.CompletedTask;
			}
			
			MessageFormatter(Formatter);
		}
		
		public void MessageFormatter(Func<Exception, HttpContext, string> formatter)
		{
			Task Formatter(Exception x, HttpContext y, HandlerContext b)
			{
				var s = formatter.Invoke(x, y);
				y.Response.WriteAsync(s);
				return Task.CompletedTask;
			}
			
			MessageFormatter(Formatter);
		}

		public void MessageFormatter(Func<Exception, HttpContext, HandlerContext, Task> formatter)
		{
			CustomFormatter = formatter;
		}
		
		public void OnError(Func<Exception, HttpContext, Task> log)
		{
			_logger = log;
		}
		
		internal RequestDelegate BuildHandler()
		{
			var handlerContext = new HandlerContext
			{
				ContentType = ContentType
			};
			
			_exceptionConfgurationTypesSortedByDepthDescending = _exceptionConfiguration.Keys
				.OrderByDescending(x => x, new ExceptionTypePolymorphicComparer())
				.ToArray();

			return async context =>
			{
				var exception = context.Features.Get<IExceptionHandlerFeature>().Error;
				if (_logger != null)
					await _logger(exception, context);
				
				if (ContentType != null)
					context.Response.ContentType = ContentType;
				
				// If any custom exceptions are set
				foreach (var type in _exceptionConfgurationTypesSortedByDepthDescending)
				{
					// ReSharper disable once UseMethodIsInstanceOfType TODO: Fire those guys
					if (type.IsAssignableFrom(exception.GetType()))
					{
						var config = ExceptionConfiguration[type];
						context.Response.StatusCode = (int)config.StatusCode;

						if (config.Formatter == null)
							config.Formatter = CustomFormatter;

						await config.Formatter(exception, context, handlerContext);
						return;
					}
				}

				// Global default format output
				if (CustomFormatter != null)
				{
					await CustomFormatter(exception, context, handlerContext);
					return;
				}

				if (DebugMode)
					await DefaultFormatter(exception, context, handlerContext);
			};
		}		
	}
}