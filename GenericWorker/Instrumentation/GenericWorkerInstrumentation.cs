using Microsoft.Practices.Unity;
using GenericWorker.Instrumentation.GenericWorkerInstrumentationWriter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using MyLibrary.Common.WorkHandling.Interfaces;

namespace GenericWorker.Instrumentation
{
    public class GenericWorkerInstrumentation : IGenericWorkerInstrumentation
    {
        #region Private Members
        private IGenericWorkerSettings _genericWorkerSettings;
        private Stopwatch _totalStopwatch;
        private Timer _timer;
        private int _writeInterval;
        private string _currentState;
        private List<WorkerStateDetail> _workerStateDetails;
        private int _intervalProcessedMessagesCount;
        private Dictionary<string, int> _intervalProcessedMessagesCountByMethod;
        #endregion

        #region Constructors
        [InjectionConstructor]
        public GenericWorkerInstrumentation(IGenericWorkerSettings genericWorkerSettings)
        {
            _genericWorkerSettings = genericWorkerSettings;
            _totalStopwatch = new Stopwatch();
            _workerStateDetails = new List<WorkerStateDetail>();
            _writeInterval = _genericWorkerSettings.InstrumentationTimer;
            _intervalProcessedMessagesCountByMethod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public GenericWorkerInstrumentation(IUnityContainer container)
        {
            _genericWorkerSettings = container.Resolve<IGenericWorkerSettings>();

            _writeInterval = _genericWorkerSettings.InstrumentationTimer;
        }
        #endregion

        #region Properties
        public string CurrentState
        {
            get
            {
                return _currentState;
            }
            set
            {
                if (_currentState != value && string.Compare(_currentState, value, true) != 0)
                {
                    var oldState = _currentState;
                    UpdateState(value);
                    OnStateChange(new StateChangeEventArgs { OldState = oldState, NewState = _currentState });
                }
            }
        }
        public int IntervalProcessedMessagesCount
        {
            get
            {
                return _intervalProcessedMessagesCount;
            }
            set
            {
                _intervalProcessedMessagesCount = value;
            }
        }
        public Dictionary<string, int> IntervalProcessedMessagesCountByMethod
        {
            get
            {
                return _intervalProcessedMessagesCountByMethod;
            }
            set
            {
                _intervalProcessedMessagesCountByMethod = value;
            }
        }
        #endregion

        #region Public Methods
        public void Start(string initialState)
        {
            _currentState = initialState;            
            _totalStopwatch.Start();

            UpdateState(initialState);

            //Elapsed Timer
            _timer = new Timer();
            _timer.Elapsed += _timer_Elapsed;
            _timer.Interval = _writeInterval;
            _timer.Start();
        }
        #endregion

        #region Event Related
        // Events
        public void Attach(IGenericWorkerInstrumentationWriter writer)
        {
            StateChange += writer.StateChange;
            InstrumentationInterval += writer.InstrumentationInterval;
        }
        public void Detach(IGenericWorkerInstrumentationWriter writer)
        {
            StateChange -= writer.StateChange;
            InstrumentationInterval += writer.InstrumentationInterval;
        }
        private event EventHandler<StateChangeEventArgs> StateChange;
        private event EventHandler<IntervalEventArgs> InstrumentationInterval;
        private void OnStateChange(StateChangeEventArgs e)
        {
            if (StateChange != null)
            {
                StateChange(this, e);
            }
        }
        private void OnInterval(IntervalEventArgs e)
        {
            if (InstrumentationInterval != null)
            {
                InstrumentationInterval(this, e);
            }
        }
        #endregion

        #region Private Methods
        private void UpdateState(string newState)
        {
            // Add the new state if it does not exist
            if (!_workerStateDetails.Any(d => String.Compare(d.Name, newState, true) == 0))
            {
                _workerStateDetails.Add(new WorkerStateDetail() { Name = newState, StopwatchActive = new Stopwatch(), StopwatchInterval = new Stopwatch() });
            }

            var newStateDetail = _workerStateDetails.Where(d => String.Compare(d.Name, newState, true) == 0).FirstOrDefault();
            var activeStateDetail = _workerStateDetails.Where(d => d.IsActive);
            foreach (var s in activeStateDetail)
            {
                s.StopwatchActive.Stop();
                s.StopwatchInterval.Stop();
            }

            newStateDetail.StopwatchActive.Start();
            newStateDetail.StopwatchInterval.Start();
            _currentState = newState;
        }
        private void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            OnInterval(new IntervalEventArgs()
            {
                TotalTime = _totalStopwatch.Elapsed,
                IntervalTime = new TimeSpan(0, 0, 0, 0, _writeInterval),
                WorkerStateDetails = _workerStateDetails,
                IntervalProcessedMessageCount = _intervalProcessedMessagesCount,
                IntervalProcessedMessageCountByMethod = _intervalProcessedMessagesCountByMethod
            });

            foreach (var s in _workerStateDetails)
            {
                s.StopwatchInterval.Reset();

                if (s.IsActive)
                {
                    s.StopwatchInterval.Start();
                }
            }

            //Reset any interval specific stats
            _intervalProcessedMessagesCount = 0;
            _intervalProcessedMessagesCountByMethod.Clear();
        }
        #endregion
    }
}
