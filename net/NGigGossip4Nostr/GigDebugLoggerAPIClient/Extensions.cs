﻿using System;
using Newtonsoft.Json;

namespace GigDebugLoggerAPIClient;

public static class Extensions
{
    public static string ToJsonString(this Exception exception)
    {
        return JsonConvert.SerializeObject(exception, new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        });
    }
}

