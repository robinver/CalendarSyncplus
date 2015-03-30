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
//  *      Modified On:    05-02-2015 12:29 PM
//  *      FileName:       CalendarUpdateService.cs
//  * 
//  *****************************************************************************/

#endregion

#region Imports

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Waf.Foundation;

using OutlookGoogleSyncRefresh.Application.Services.Google;
using OutlookGoogleSyncRefresh.Application.Utilities;
using OutlookGoogleSyncRefresh.Application.Wrappers;
using OutlookGoogleSyncRefresh.Common.Log;
using OutlookGoogleSyncRefresh.Common.MetaData;
using OutlookGoogleSyncRefresh.Domain.Models;

#endregion

namespace OutlookGoogleSyncRefresh.Application.Services.CalendarUpdate
{
    [Export(typeof(ICalendarUpdateService))]
    public class CalendarUpdateService : Model, ICalendarUpdateService
    {
        #region Fields

        private readonly ApplicationLogger _applicationLogger;

        private Appointment _currentAppointment;
        private CalendarAppointments _destinationAppointments;
        private CalendarAppointments _sourceAppointments;
        private string _syncStatus;

        #endregion

        #region Constructors

        [ImportingConstructor]
        public CalendarUpdateService(ICalendarServiceFactory calendarServiceFactory, ApplicationLogger applicationLogger)
        {
            _applicationLogger = applicationLogger;
            CalendarServiceFactory = calendarServiceFactory;
        }

        #endregion

        #region Properties

        public ICalendarServiceFactory CalendarServiceFactory { get; set; }

        #endregion

        #region Private Methods
        /// <summary>
        /// 
        /// </summary>
        /// <param name="daysInPast"></param>
        /// <param name="daysInFuture"></param>
        /// <param name="sourceCalendarSpecificData"></param>
        /// <param name="destinationCalendarSpecificData"></param>
        /// <returns></returns>
        private bool LoadAppointments(int daysInPast, int daysInFuture,
            IDictionary<string, object> sourceCalendarSpecificData, IDictionary<string, object> destinationCalendarSpecificData)
        {
            //Update status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.SourceAppointmentsReading, SourceCalendarService.CalendarServiceName);
            //Get source calendar
            SourceAppointments = SourceCalendarService.GetCalendarEventsInRangeAsync(daysInPast, daysInFuture, sourceCalendarSpecificData).Result;
            if (SourceAppointments == null)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.SourceAppointmentsReadFailed);
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return false;
            }
            //Update status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.SourceAppointmentsRead, SourceCalendarService.CalendarServiceName, SourceAppointments.Count);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.DestAppointmentReading, DestinationCalendarService.CalendarServiceName);

            //Get destination calendar
            DestinationAppointments = DestinationCalendarService.GetCalendarEventsInRangeAsync(daysInPast, daysInFuture,
                        destinationCalendarSpecificData).Result;
            if (DestinationAppointments == null)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.DestAppointmentReadFailed);
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return false;
            }
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.DestAppointmentRead, DestinationCalendarService.CalendarServiceName, DestinationAppointments.Count);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="sourceList"></param>
        /// <param name="destinationList"></param>
        /// <returns></returns>
        private List<Appointment> GetAppointmentsToDelete(SyncProfile syncProfile,
            List<Appointment> sourceList, List<Appointment> destinationList)
        {
            bool addDescription =
                    syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Description);
            var appointmentsToDelete = new List<Appointment>();
            foreach (var destAppointment in destinationList)
            {
                if (syncProfile.SyncSettings.SyncMode == SyncModeEnum.TwoWay && destAppointment.SourceId == null)
                {
                    continue;
                }

                bool isFound = false;
                foreach (var sourceAppointment in sourceList)
                {
                    bool isCopy = sourceAppointment.CompareSourceId(destAppointment) ||
                                  destAppointment.CompareSourceId(sourceAppointment);
                    //Check if destination entry is a copy of source entry
                    //Or if source entry is a copy of destination entry

                    //If both entries have same content
                    if (destAppointment.Equals(sourceAppointment))
                    {
                        if (addDescription)
                        {
                            if (sourceAppointment.CompareDescription(destAppointment))
                            {
                                isFound = true;
                            }
                        }
                        else
                        {
                            isFound = true;
                        }
                    }

                    if (isFound)
                    {
                        break;
                    }

                    if (isCopy)
                    {
                        if (syncProfile.SyncSettings.SyncMode == SyncModeEnum.TwoWay && syncProfile.SyncSettings.KeepLastModifiedVersion)
                        {
                            if (destAppointment.LastModified.HasValue && sourceAppointment.LastModified.HasValue)
                            {
                                if (destAppointment.LastModified.Value > sourceAppointment.LastModified.Value)
                                {
                                    isFound = true;
                                }
                            }
                        }
                        break;
                    }

                }
                //If no entry is found in source, delete the entries in the destination
                if (!isFound)
                {
                    appointmentsToDelete.Add(destAppointment);
                }
            }
            return appointmentsToDelete;

        }

        /// <summary>
        /// Gets appointments to add in the destination calendar
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="sourceList"></param>
        /// <param name="destinationList"></param>
        /// <returns></returns>
        private List<Appointment> GetAppointmentsToAdd(SyncProfile syncProfile, List<Appointment> sourceList,
            List<Appointment> destinationList)
        {

            if (destinationList.Any())
            {
                bool addDescription =
                    syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Description);
                var appointmentsToAdd = new List<Appointment>();
                foreach (var sourceAppointment in sourceList)
                {
                    bool isFound = false;
                    foreach (var destAppointment in destinationList)
                    {
                        //Check if both entries have same content
                        if (destAppointment.Equals(sourceAppointment))
                        {
                            if (addDescription)
                            {
                                if (sourceAppointment.CompareDescription(destAppointment))
                                {
                                    isFound = true;
                                }
                            }
                            else
                            {
                                isFound = true;
                            }
                        }

                        if (isFound)
                        {
                            break;
                        }

                    }
                    //Add the entry if no entry matching in destination is found
                    if (!isFound)
                    {
                        appointmentsToAdd.Add(sourceAppointment);
                    }
                }

                return appointmentsToAdd;
            }
            return sourceList;
        }

        private void InitiatePreSyncSetup(SyncProfile syncProfile)
        {
            SourceCalendarService = CalendarServiceFactory.GetCalendarService(syncProfile.SyncSettings.SourceCalendar);
            DestinationCalendarService = CalendarServiceFactory.GetCalendarService(syncProfile.SyncSettings.DestinationCalendar);
        }

        private IDictionary<string, object> GetCalendarSpecificData(CalendarServiceType serviceType, SyncProfile syncProfile)
        {
            switch (serviceType)
            {
                case CalendarServiceType.Google:
                    return new Dictionary<string, object> { { "CalendarId", syncProfile.GoogleCalendar.Id } };
                case CalendarServiceType.OutlookDesktop:
                    return new Dictionary<string, object>
                    {
                        { "ProfileName", syncProfile.OutlookSettings.OutlookProfileName },
                        { "OutlookCalendar", syncProfile.OutlookSettings.OutlookCalendar }
                    };
                case CalendarServiceType.EWS:
                    return null;
            }
            return null;
        }
        private void LoadSourceId()
        {
            if (SourceAppointments.Any())
            {
                string calendarId = DestinationAppointments.CalendarId;
                foreach (var sourceAppointment in SourceAppointments)
                {
                    sourceAppointment.LoadSourceId(calendarId);
                }
            }

            if (DestinationAppointments.Any())
            {
                string calendarId = SourceAppointments.CalendarId;
                foreach (var destAppointment in DestinationAppointments)
                {
                    destAppointment.LoadSourceId(calendarId);
                }
            }
        }

        /// <summary>
        /// Add appointments to destination
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="destinationCalendarSpecificData"></param>
        /// <returns></returns>
        private bool AddDestinationAppointments(SyncProfile syncProfile, IDictionary<string, object> destinationCalendarSpecificData)
        {
            //Update status for reading entries to add
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.ReadingEntriesToAdd);
            //Get entries to add
            List<Appointment> appointmentsToAdd = GetAppointmentsToAdd(syncProfile, SourceAppointments, DestinationAppointments);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.EntriesToAdd, appointmentsToAdd.Count);
            if (appointmentsToAdd.Count == 0)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return true;
            }
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.AddingEntries, DestinationCalendarService.CalendarServiceName);
            //Add entries to destination calendar
            bool isSuccess = DestinationCalendarService.AddCalendarEvent(appointmentsToAdd,
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Description),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Reminders),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Attendees),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.AttendeesToDescription), destinationCalendarSpecificData)
                .Result;
            //Update status if entries were successfully added
            SyncStatus =
                StatusHelper.GetMessage(isSuccess ? SyncStateEnum.AddEntriesComplete : SyncStateEnum.AddEntriesFailed);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            return isSuccess;
        }
        /// <summary>
        /// Delete appointments in destination
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="destinationCalendarSpecificData"></param>
        /// <returns></returns>
        private bool DeleteDestinationAppointments(SyncProfile syncProfile,
            IDictionary<string, object> destinationCalendarSpecificData, SyncCallback syncCallback)
        {
            if (syncProfile.SyncSettings.DisableDelete)
            {
                return true;
            }
            //Updating entry delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.ReadingEntriesToDelete);
            //Getting appointments to delete
            List<Appointment> appointmentsToDelete = GetAppointmentsToDelete(syncProfile, SourceAppointments, DestinationAppointments);
            //Updating Get entry delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.EntriesToDelete, appointmentsToDelete.Count);

            if (appointmentsToDelete.Count == 0)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return true;
            }

            if (syncProfile.SyncSettings.ConfirmOnDelete && syncCallback != null)
            {
                string message = string.Format("Are you sure you want to delete {0} items from {1}?",
                    appointmentsToDelete.Count, DestinationCalendarService.CalendarServiceName);
                SyncEventArgs e = new SyncEventArgs(message, UserActionEnum.ConfirmDelete);
                var task = syncCallback(e);
                if (!task.Result)
                {
                    SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                    return true;
                }
            }

            //Updating delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.DeletingEntries, DestinationCalendarService.CalendarServiceName);

            //Deleting entries
            bool isSuccess =
                DestinationCalendarService.DeleteCalendarEvent(appointmentsToDelete, destinationCalendarSpecificData).Result;
            //Update status if entries were successfully deleted
            SyncStatus =
                StatusHelper.GetMessage(isSuccess ? SyncStateEnum.DeletingEntriesComplete : SyncStateEnum.DeletingEntriesFailed);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            if (isSuccess)
            {
                for (int index = 0; index < appointmentsToDelete.Count; index++)
                {
                    DestinationAppointments.Remove(appointmentsToDelete[index]);
                }
            }
            return isSuccess;
        }
        /// <summary>
        /// Add appointments to source
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="sourceCalendarSpecificData"></param>
        /// <returns></returns>
        private bool AddSourceAppointments(SyncProfile syncProfile, IDictionary<string, object> sourceCalendarSpecificData)
        {
            //Update status for reading entries to add
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.ReadingEntriesToAdd);
            //Get entries to add
            List<Appointment> appointmentsToAdd = GetAppointmentsToAdd(syncProfile, DestinationAppointments, SourceAppointments);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.EntriesToAdd, appointmentsToAdd.Count);
            if (appointmentsToAdd.Count == 0)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return true;
            }
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.AddingEntries, SourceCalendarService.CalendarServiceName);

            //Add entries to calendar
            bool isSuccess = SourceCalendarService.AddCalendarEvent(appointmentsToAdd,
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Description),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Reminders),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.Attendees),
                syncProfile.CalendarEntryOptions.HasFlag(CalendarEntryOptionsEnum.AttendeesToDescription), sourceCalendarSpecificData)
                .Result;
            //Update status if entries were successfully added
            SyncStatus =
                StatusHelper.GetMessage(isSuccess ? SyncStateEnum.AddEntriesComplete : SyncStateEnum.AddEntriesFailed);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);

            return isSuccess;
        }

        /// <summary>
        /// Delete appointments from source
        /// </summary>
        /// <param name="syncProfile"></param>
        /// <param name="sourceCalendarSpecificData"></param>
        /// <returns></returns>
        private bool DeleteSourceAppointments(SyncProfile syncProfile, IDictionary<string, object> sourceCalendarSpecificData, SyncCallback syncCallback)
        {
            if (syncProfile.SyncSettings.DisableDelete)
            {
                return true;
            }
            //Updating entry delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.ReadingEntriesToDelete);
            //Getting appointments to delete
            List<Appointment> appointmentsToDelete = GetAppointmentsToDelete(syncProfile, DestinationAppointments, SourceAppointments);
            //Updating Get entry delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.EntriesToDelete, appointmentsToDelete.Count);
            if (appointmentsToDelete.Count == 0)
            {
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                return true;
            }

            if (syncProfile.SyncSettings.ConfirmOnDelete && syncCallback != null)
            {
                string message = string.Format("Are you sure you want to delete {0} items from {1}?",
                    appointmentsToDelete.Count, DestinationCalendarService.CalendarServiceName);
                SyncEventArgs e = new SyncEventArgs(message, UserActionEnum.ConfirmDelete);
                var task = syncCallback(e);
                if (!task.Result)
                {
                    SyncStatus = StatusHelper.GetMessage(SyncStateEnum.SkipDelete);
                    SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                    return true;
                }
            }
            //Updating delete status
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.DeletingEntries, SourceCalendarService.CalendarServiceName);
            //Deleting entries
            bool isSuccess =
                SourceCalendarService.DeleteCalendarEvent(appointmentsToDelete, sourceCalendarSpecificData).Result;
            //Update status if entries were successfully deleted
            SyncStatus =
                StatusHelper.GetMessage(isSuccess ? SyncStateEnum.DeletingEntriesComplete : SyncStateEnum.DeletingEntriesFailed);
            SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
            if (isSuccess)
            {
                for (int index = 0; index < appointmentsToDelete.Count; index++)
                {
                    SourceAppointments.Remove(appointmentsToDelete[index]);
                }
            }
            return isSuccess;
        }
        #endregion

        #region ICalendarUpdateService Members

        public CalendarAppointments DestinationAppointments
        {
            get { return _destinationAppointments; }
            set { SetProperty(ref _destinationAppointments, value); }
        }

        public CalendarAppointments SourceAppointments
        {
            get { return _sourceAppointments; }
            set { SetProperty(ref _sourceAppointments, value); }
        }

        public Appointment CurrentAppointment
        {
            get { return _currentAppointment; }
            set { SetProperty(ref _currentAppointment, value); }
        }

        public string SyncStatus
        {
            get { return _syncStatus; }
            set { SetProperty(ref _syncStatus, value); }
        }

        public ICalendarService SourceCalendarService { get; set; }

        public ICalendarService DestinationCalendarService { get; set; }

        public bool SyncCalendar(SyncProfile syncProfile, SyncCallback syncCallback)
        {
            InitiatePreSyncSetup(syncProfile);

            bool isSuccess = false;
            if (syncProfile != null)
            {
                //Add log for sync mode
                SyncStatus = string.Format("Calendar Sync : {0} {2} {1}", SourceCalendarService.CalendarServiceName, DestinationCalendarService.CalendarServiceName,
                    syncProfile.SyncSettings.SyncMode == SyncModeEnum.TwoWay ? "<===>" : "===>");
                SyncStatus = StatusHelper.GetMessage(SyncStateEnum.Line);
                //Add log for date range
                SyncStatus = string.Format("Date Range - {0} - {1}",
                    DateTime.Now.Subtract(new TimeSpan(syncProfile.DaysInPast, 0, 0, 0)).ToString("D"),
                    DateTime.Now.Add(new TimeSpan(syncProfile.DaysInFuture, 0, 0, 0)).ToString("D"));

                //Load calendar specific data
                var sourceCalendarSpecificData = GetCalendarSpecificData(syncProfile.SyncSettings.SourceCalendar, syncProfile);
                var destinationCalendarSpecificData = GetCalendarSpecificData(syncProfile.SyncSettings.DestinationCalendar, syncProfile);

                //Get source and destination appointments
                isSuccess = LoadAppointments(syncProfile.DaysInPast, syncProfile.DaysInFuture, sourceCalendarSpecificData,
                            destinationCalendarSpecificData);

                if (isSuccess)
                {
                    LoadSourceId();
                }

                if (isSuccess)
                {
                    //Delete destination appointments
                    isSuccess = DeleteDestinationAppointments(syncProfile, destinationCalendarSpecificData, syncCallback);
                }

                if (isSuccess)
                {
                    //Add appointments to destination
                    isSuccess = AddDestinationAppointments(syncProfile, destinationCalendarSpecificData);
                }

                if (isSuccess && syncProfile.SyncSettings.SyncMode == SyncModeEnum.TwoWay)
                {
                    //Delete destination appointments
                    isSuccess = DeleteSourceAppointments(syncProfile, sourceCalendarSpecificData, syncCallback);

                    if (isSuccess)
                    {
                        //If sync mode is two way... add events to source
                        isSuccess = AddSourceAppointments(syncProfile, sourceCalendarSpecificData);
                    }
                }
            }
            SourceAppointments = null;
            DestinationAppointments = null;
            SourceCalendarService = null;
            DestinationCalendarService = null;
            return isSuccess;
        }

        #endregion
    }
}