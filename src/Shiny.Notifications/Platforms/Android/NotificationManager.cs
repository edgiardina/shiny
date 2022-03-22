using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Shiny.Infrastructure;
using Shiny.Locations;


namespace Shiny.Notifications
{
    public partial class NotificationManager : INotificationManager
    {
        readonly ShinyCoreServices core;
        readonly AndroidNotificationManager manager;
        readonly IGeofenceManager geofenceManager;


        public NotificationManager(ShinyCoreServices core,
                                   AndroidNotificationManager manager,
                                   IGeofenceManager geofenceManager)
        {
            this.core = core;
            this.manager = manager;
            this.geofenceManager = geofenceManager;

            this.core
                .Platform
                .WhenIntentReceived()
                .SubscribeAsync(x => this
                    .core
                    .Services
                    .Resolve<AndroidNotificationProcessor>()!
                    .TryProcessIntent(x)
                );
        }


        public async Task Cancel(int id)
        {
            var notification = await this.core.Repository.Get<Notification>(id.ToString());
            if (notification != null)
            {
                await this.CancelInternal(notification);
                await this.core.Repository.Remove<Notification>(id.ToString());
            }
        }


        public async Task Clear()
        {
            var notifications = await this.core.Repository.GetList<Notification>();
            foreach (var notification in notifications) 
                await this.CancelInternal(notification);
            
            await this.core.Repository.Clear<Notification>();
        }


        public async Task<IEnumerable<Notification>> GetPending()
            => await this.core.Repository.GetList<Notification>().ConfigureAwait(false);


        public async Task<AccessState> RequestAccess(bool locationAware)
        {
            if (!this.manager.NativeManager.AreNotificationsEnabled())
                return AccessState.Disabled;
            
            if (locationAware)
            {
                var locPermission = await this.geofenceManager.RequestAccess();
                if (locPermission != AccessState.Available)
                    return AccessState.Restricted;
            }
            if (!this.Alarms.CanScheduleExactAlarms())
                return AccessState.Restricted;

            return AccessState.Available;
        }


        public async Task Send(Notification notification)
        {
            if (notification.Id == 0)
                notification.Id = this.core.Settings.IncrementValue("NotificationId");

            notification.AssertValid();

            // this is here to cause validation of the settings before firing or scheduling
            var channel = await this.GetChannel(notification);
            var builder = this.manager.CreateNativeBuilder(notification, channel);

            if (notification.Geofence != null)
            {
                await this.geofenceManager.StartMonitoring(new GeofenceRegion(
                    NotificationGeofenceDelegate.GetGeofenceId(notification),
                    notification.Geofence!.Center!,
                    notification.Geofence!.Radius!
                ));
            }

            // HACK: geofence delegate nullifies geofence coming back in for send
            if (notification.ScheduleDate == null && notification.Geofence == null)
            {
                this.manager.SendNative(notification.Id, builder.Build());
                if (notification.BadgeCount != null)
                    this.core.SetBadgeCount(notification.BadgeCount.Value);
            }
            else
            {
                await this.core.Repository.Set(notification.Id.ToString(), notification);
            }
        }


        public int Badge
        {
            get => this.core.GetBadgeCount();
            set => this.core.SetBadgeCount(value);
        }


        protected virtual void SetAlarm(Notification notification)
        {
            var pendingIntent = this.GetAlarmPendingIntent(notification);
            var millis = (notification.ScheduleDate!.Value.ToUniversalTime() - DateTime.UtcNow).TotalMilliseconds;
            this.Alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, (long)millis, pendingIntent);
        }


        protected virtual async Task CancelInternal(Notification notification)
        {
            if (notification.Geofence != null)
            {
                var geofenceId = NotificationGeofenceDelegate.GetGeofenceId(notification);
                await this.geofenceManager.StopMonitoring(geofenceId);
            }
            if (notification.ScheduleDate != null || notification.RepeatInterval != null)
                this.Alarms.Cancel(this.GetAlarmPendingIntent(notification));
            
            this.manager.NativeManager.Cancel(notification.Id);
        }


        protected virtual PendingIntent GetAlarmPendingIntent(Notification notification)
        {
            var intent = this.core.Platform.CreateIntent<ShinyNotificationBroadcastReceiver>(ShinyNotificationBroadcastReceiver.AlarmIntentAction);
            intent.PutExtra(AndroidNotificationProcessor.IntentNotificationKey, notification.Id);
            var pendingIntent = PendingIntent.GetBroadcast(
                this.core.Platform.AppContext,
                notification.Id,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable
            );
            return pendingIntent!;
        }


        AlarmManager? alarms;
        protected AlarmManager Alarms => this.alarms ??= this.core.Platform.GetSystemService<AlarmManager>(Context.AlarmService);
    }
}
