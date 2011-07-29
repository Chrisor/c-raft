﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace Chraft.Plugins.Events
{
    public class EventList : IEnumerable<ChraftEventHandler>
    {
        private List<ChraftEventHandler> Events = new List<ChraftEventHandler>();
        private Dictionary<Event, ChraftEventHandler> Mappings = new Dictionary<Event, ChraftEventHandler>();
        
        public EventList() { }
        /// <summary>
        /// Gets an instance of the Event Handler holding the given event.
        /// </summary>
        /// <param name="Event">The name of the event(e.g. PLUGIN_ENABLED).</param>
        /// <returns>Event Handler</returns>
        public ChraftEventHandler Find(Event Event)
        {
            foreach (ChraftEventHandler e in Events)
            {
                if (e.Events.Contains(Event))
                {
                    return e;
                }
            }
            throw new EventNotFoundException("\"" + Event + "\" was not found.  Please ask the developer(s) to fix this error.");
            //Insted of returning null we throw a EventNotFoundException.  
            //Yep... we will not be getting a NullRefrenceException here.
            //return null;
        }
        /// <summary>
        /// Adds an event handler.
        /// </summary>
        /// <param name="e">The Event handler to add.</param>
        public void Add(ChraftEventHandler e)
        {
            Events.Add(e);
            foreach (Event Event in e.Events)
            {
                Mappings.Add(Event, e);
            }
        }
        /// <summary>
        /// Removes a single event from the mappings.  Any further calles to this event will return a EventNotFoundException.
        /// </summary>
        /// <param name="Event">The name of the event to remove.</param>
        public void Remove(Event Event)
        {
            Mappings.Remove(Event);
        }
        /// <summary>
        /// Removes an event handler.
        /// 
        /// This removes the event handler and ALL Mappings linked to this handler.
        /// </summary>
        /// <param name="e"></param>
        public void RemoveEventHandler(ChraftEventHandler e)
        {
            foreach(KeyValuePair<Event, ChraftEventHandler> ed in from ed in Mappings 
                    where Mappings.ContainsValue(e) select ed)
            {
                Mappings.Remove(ed.Key);
            }
            Events.Remove(e);
        }

        public IEnumerator<ChraftEventHandler> GetEnumerator()
        {
            return Events.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
           return GetEnumerator();
        }
    }
    /// <summary>
    /// A custom Exception to handle non-existant events.
    /// </summary>
    public class EventNotFoundException : Exception
    {
        public EventNotFoundException() { }
        public EventNotFoundException(string message) : base(message) { }
        public EventNotFoundException(string message, Exception inner) : base(message, inner) { }
    }
}
