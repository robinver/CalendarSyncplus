﻿#region File Header

// /******************************************************************************
//  * 
//  *      Copyright (C) Ankesh Dave 2015 All Rights Reserved. Confidential
//  * 
//  ******************************************************************************
//  * 
//  *      Project:        OutlookGoogleSyncRefresh
//  *      SubProject:     OutlookGoogleSyncRefresh.Application
//  *      Author:         Dave, Ankesh
//  *      Created On:     03-02-2015 7:31 PM
//  *      Modified On:    05-02-2015 12:40 PM
//  *      FileName:       ICalendarUpdateService.cs
//  * 
//  *****************************************************************************/

#endregion

#region Imports

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using OutlookGoogleSyncRefresh.Domain.Models;

#endregion

namespace OutlookGoogleSyncRefresh.Application.Services.CalendarUpdate
{
    public interface ICalendarUpdateService : INotifyPropertyChanged
    {
        #region Properties

        List<Appointment> GoogleAppointments { get; set; }
        List<Appointment> OutlookAppointments { get; set; }
        Appointment CurrentAppointment { get; set; }
        string SyncStatus { get; set; }
        #endregion

        #region Public Methods

        Task<bool> SyncCalendarAsync(Settings settings);

        #endregion
    }
}