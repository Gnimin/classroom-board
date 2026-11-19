using System;
using System.Collections.Generic;
using System.Linq;
using NEWCLASSroom.Models;

namespace NEWCLASSroom.Services
{
    /// <summary>消息的排队与历史记录。真正的显示交给 MessageOverlay 窗口。</summary>
    public class MessageService
    {
        private readonly Queue<BannerMessage> _queue = new Queue<BannerMessage>();
        private readonly List<BannerMessage> _history = new List<BannerMessage>();
        private readonly object _lock = new object();

        /// <summary>有新消息进入队列。</summary>
        public event EventHandler Enqueued;

        public int PendingCount
        {
            get { lock (_lock) { return _queue.Count; } }
        }

        public List<BannerMessage> History
        {
            get { lock (_lock) { return _history.ToList(); } }
        }

        /// <summary>把一条消息放进待显示队列并记入历史。</summary>
        public void Enqueue(BannerMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Text)) return;
            lock (_lock)
            {
                _queue.Enqueue(message);
                _history.Insert(0, message);
                if (_history.Count > 60) _history.RemoveRange(60, _history.Count - 60);
            }
            EventHandler handler = Enqueued;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>取出一条待显示的消息，没有就返回空。</summary>
        public BannerMessage Dequeue()
        {
            lock (_lock)
            {
                return _queue.Count > 0 ? _queue.Dequeue() : null;
            }
        }

        /// <summary>清空待显示队列。</summary>
        public void Clear()
        {
            lock (_lock) { _queue.Clear(); }
        }
    }
}
