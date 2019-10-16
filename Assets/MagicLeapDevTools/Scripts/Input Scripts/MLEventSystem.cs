///********************************************************///
///-Please note that OnInitializePotentialDrag has not been 
///integrated into the event logic, so you can only use
///OnBeginDrag
///-If the drag system being used relies on forces applied
///to a rigid body, then the onDrag call and its
///ssurrounding logic need to be put in fixed update
///********************************************************///

using UnityEngine;
using UnityEngine.XR.MagicLeap;

namespace MtsuMLAR
{
    /// <summary>
    /// The MLEventSystem takes info from the Input module and determines when certain events need
    /// to be fired and at which objects. It calls the event only if the object has a script that 
    /// implements the matching interface.
    /// </summary>
    [RequireComponent(typeof(MLInputModuleV2))]
    public class MLEventSystem : MonoBehaviour
    {
        enum clickButton { trigger, bumper }

        #region Private Variables
        [SerializeField, Tooltip("Sets primary click, secondary click auto set")]
        private clickButton primaryClick;

        private GameObject lastHitObject = null;      //Object hit last frame, compared to the InputModule current hit to determine transitions
        private GameObject lastSelectedObject = null; //Object considered selected
        private bool isDragging = false;              //Drag Flag, bypasses some logic and is used by some external scripts
        private GameObject draggedObject = null;      //Object currently being dragged

        private GameObject clickDownObject = null;    //Used to see if a button is released on the same object
        private GameObject click_2_DownObject = null;
        private float bumperTimer;                    //Time between button down and up, used to see if a button up event is considered a click
        private float triggerTimer;
        private MLEventData eventData;                //Data sent to objects when events are called

        private MLInputModuleV2 inputModule;          //Script that determines what is being pointed at based on current input method

        private MLInputController _controller;                //Object used to access controller events

        /// Variables to cache references to event handlers called every frame,
        /// so calling GetComponent() every frame can be avoided
        private IMLPointerStayHandler stayHandler = null;
        private IMLUpdateSelectedHandler updateSelectedHandler = null;
        private IMLInitializePotentialDragHandler potentialDragHandler = null;
        private IMLDragHandler dragHandler = null;

        [SerializeField, Tooltip("Set the time window in which a click must be completed for it to register")]
        private float clickWindow = 0.8f;
        #endregion

        #region Public Properties
        public GameObject LastHitObject { get => lastHitObject; }
        public GameObject LastSelectedObject { get => lastSelectedObject; }
        public bool IsDragging { get => isDragging; }
        public GameObject DraggedObject { get => draggedObject; }
        #endregion

        #region Unity Methods
        private void Awake()
        {
            primaryClick = clickButton.trigger;
            eventData = new MLEventData();
        }

        // Start initializes references, the MLInput API, and event subscriptions
        void Start()
        {
            if (!MLInput.Start().IsOk)
            {
                Debug.LogWarning("MLInput failed to start, disabling MLEventSystem");
                enabled = false;
            }

            _controller = MLInput.GetController(MLInput.Hand.Left);
            if(_controller == null)
            {
                _controller = MLInput.GetController(MLInput.Hand.Right);
                if(_controller == null)
                {
                    Debug.LogWarning("Couldn't get a reference to the Controller, disabling MLEventSystem");
                    enabled = false;
                }
            }

            MLInput.OnControllerButtonDown += ControllerButtonDownHandler;
            MLInput.OnControllerButtonUp += ControllerButtonUpHandler;
            MLInput.OnTriggerDown += TriggerDownHandler;
            MLInput.OnTriggerUp += TriggerUpHandler;

            inputModule = GetComponent<MLInputModuleV2>();
            if(inputModule == null)
            {
                Debug.LogWarning("Couldn't get a reference to the InputModule, disabling script");
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            MLInput.OnControllerButtonDown -= ControllerButtonDownHandler;
            MLInput.OnControllerButtonUp -= ControllerButtonUpHandler;
            MLInput.OnTriggerDown -= TriggerDownHandler;
            MLInput.OnTriggerUp -= TriggerUpHandler;

            MLInput.Stop();
        }

        /// <summary>
        /// The update function takes the current hit object from the input module and compares it
        /// to the last hit object in order to detect transitions and call necessary events.
        /// </summary>
        void Update()
        {
            UpdateEventData(eventData);

            //If we are dragging, then we aren't interested in interacting with new objects
            if (!isDragging)
            {
                if (inputModule.CurrentHitState != MLInputModuleV2.HitState.NoHit)
                {
                    //The current hit object to compare with
                    GameObject hitObject = SearchForEventHandlerInAncestors(inputModule.PrimaryHitObject);
                    if (hitObject == null)
                        hitObject = inputModule.PrimaryHitObject;

                    //Hit new object
                    if (lastHitObject == null)
                    {
                        //Call handler if it exists
                        IMLPointerEnterHandler enterHandler = hitObject.GetComponent<IMLPointerEnterHandler>();
                        if (enterHandler != null)
                        {
                            enterHandler.MLOnPointerEnter(eventData);
                        }
                        //cache handler reference to be called every frame
                        stayHandler = hitObject.GetComponent<IMLPointerStayHandler>();
                        lastHitObject = hitObject;
                    }
                    //Hit same object
                    else if (hitObject == lastHitObject)
                    {
                        //Call handler if it exists
                        if (stayHandler != null)
                        {
                            stayHandler.MLOnPointerStay(eventData);
                        }
                    }
                    else //We left an object
                    {
                        //Call handler if it exists
                        IMLPointerExitHandler exitHandler = lastHitObject.GetComponent<IMLPointerExitHandler>();
                        if (exitHandler != null)
                        {
                            exitHandler.MLOnPointerExit(eventData);
                        }
                        
                        lastHitObject = null; //This causes 
                    }
                }
                else //If the raycaster is not hitting an object, but a UI element or nothing, then do this:
                {
                    //If we switched from hitting an object to not, call its exit handler
                    if (lastHitObject != null)
                    {
                        //chack and call the handler
                        IMLPointerExitHandler exitHandler = lastHitObject.GetComponent<IMLPointerExitHandler>();
                        if (exitHandler != null)
                        {
                            exitHandler.MLOnPointerExit(eventData);
                        }
                        lastHitObject = null;
                    }
                }
            }
            else  //isDragging == true
            {
                //****If the drag update is physics base, then this must be placed in fixed updat****

                //When a drag starts, the dragged object stays the lastHitObject, since the event system stops updating it until the drag stops
                //If a drag starts, its basically assured there is an object to reference, but this makes sure it hasn't been spontaneously deleted
                if (lastHitObject != null)
                {
                    //check and call the handler
                    if (dragHandler != null)
                    {
                        dragHandler.MLOnDrag(eventData);
                    }
                }
            }

            //Every frame, if an object is selected, check to see if it has a  handler for selectedupdate, regardless of isDragging value
            if (lastSelectedObject != null)
            {
                if (updateSelectedHandler != null)
                {
                    updateSelectedHandler.MLOnUpdateSelected(eventData);
                }
            }
        }
        #endregion //Unity Methods

        #region Private Methods
        /// <summary>
        /// This function updates the event Data class, which will allow recieving methods
        /// to make use of current raycaster information and selected object by the event
        /// system. The drag handler needs the raycaster transform for example, in order
        /// to follow it
        /// </summary>
        /// <param name="eventData">Class containing important data for the recieving methods to use</param>
        private void UpdateEventData(MLEventData eventData)
        {
            eventData.CurrentSelectedObject = lastSelectedObject;
            //eventData.PointerRayHitInfo = _raycaster.Hit;
            //eventData.PointerTransform = _raycaster.transform;
            eventData.PointerTransform = inputModule.PrimaryInputPointerObject.transform;
            eventData.CurrentHitObject = inputModule.PrimaryHitObject;
        }

        /// <summary>
        /// This recursively searches an object and its ancestors for  an eventhandler,
        /// allowing for colliders placed on children of an object with an event handlling behavior
        /// to be registered as an interactable object by the input system.
        /// </summary>
        /// <param name="searchObject">object to begin the search for an eventHandler</param>
        private GameObject SearchForEventHandlerInAncestors(GameObject searchObject)
        {
            if (searchObject?.GetComponent<IMLEventHandler>() != null)
                return searchObject;
            else
            {
                searchObject = searchObject.transform.parent?.gameObject;
                while(searchObject != null)
                {
                    if (searchObject.GetComponent<IMLEventHandler>() != null)
                    {
                        return searchObject;
                    }
                    else
                    {
                        searchObject = searchObject.transform.parent?.gameObject;
                    }
                }
                return null;
            }
        }
        #endregion

        #region Button Handlers
        /// <summary>
        /// This function takes the button down event from the controller and decides what events to call based on
        /// other event system values, like the click window, primary button state, or the last hit object
        /// </summary>
        /// <param name="controllerID">Specific ID of the controller that sent the event</param>
        /// <param name="button">This enum contains what button sent the event</param>
        public void ControllerButtonDownHandler(byte controllerID, MLInputControllerButton button)
        {
            //Some controller validation, and making sure the event is from the bumper, else we don't do anything
            if (_controller != null && _controller.Id == controllerID && button == MLInputControllerButton.Bumper)
            {
                UpdateEventData(eventData);
                //Set the start time to test if when the button is released it was quick enough to be considered a click
                bumperTimer = Time.time;

                /* If this is the primary button
                 * Then it already started the drag, and shouldn't call any more button down events anyway. If it is
                 * the secondary button, then we aren't interested in any of its clicks during a drag
                 * also, only try to send events if the raycaster says we aren't hitting UI, because even if it hits
                 * nothing, it has to call exit handlers*/
                if (!isDragging)
                {
                    //This checks if the button was pressed while pointing at an object
                    if (lastHitObject != null)
                    {
                        //If the bumper is the primary clicker, then execute this code
                        if (primaryClick == clickButton.bumper)
                        {
                            //This reference allows the system to check if a click was released on the same object it started on, a criteria for some events
                            clickDownObject = lastHitObject;
                            //Get, check, and call the down handler on the object
                            IMLPointerDownHandler downHandler = lastHitObject.GetComponent<IMLPointerDownHandler>();
                            if (downHandler != null)
                            {
                                downHandler.MLOnPointerDown(eventData);
                            }
                            //If you click on a draggable object, then you may want to start a drag
                            //So check if it has this handler
                            IMLInitializePotentialDragHandler potentialDragHandler = lastHitObject.GetComponent<IMLInitializePotentialDragHandler>();
                            if (potentialDragHandler != null)
                            {
                                potentialDragHandler.MLOnInitializePotentialDrag(eventData);
                                                                                                     //If the object has a drag initializer, then a drag must be initiated through criteria evaluated elsewhere
                            }
                            else //The object has no potentialDragInitializer, so just start the drag
                            {
                                //For an object to be considered draggable, it must implement the beginDrag interface, as well as the Drag
                                IMLBeginDragHandler beginDragHandler = lastHitObject.GetComponent<IMLBeginDragHandler>();
                                if (beginDragHandler != null)
                                {
                                    beginDragHandler.MLOnBeginDrag(eventData);
                                    isDragging = true;
                                    draggedObject = lastHitObject;
                                    //This assignment prevents the raycast from entering drag state without a prev hit object
                                    //_raycaster.PrevHitObject = lastHitObject;
                                    dragHandler = lastHitObject.GetComponent<IMLDragHandler>(); //cache reference to drag handler
                                                                                                //If the object about to be dragged has not all ready been selected, then do this
                                    if (LastHitObject != lastSelectedObject)
                                    {
                                        //This is done so that the last selected object is no longer selected once a drag starts
                                        IMLDeselectHandler deselectHandler = lastSelectedObject?.GetComponent<IMLDeselectHandler>();
                                        if (deselectHandler != null)
                                        {
                                            deselectHandler.MLOnDeselect(eventData);
                                            lastSelectedObject = null;
                                        }
                                    }
                                }
                            }
                        }
                        else //bumper == click_2, not primary
                        {
                            click_2_DownObject = lastHitObject;//To be checked before select and click is called in up handler
                                                               //Get, check, and call handler:
                            IMLPointer_2_DownHandler down_2_Handler = lastHitObject.GetComponent<IMLPointer_2_DownHandler>();
                            if (down_2_Handler != null)
                            {
                                down_2_Handler.MLOnPointer_2_Down(eventData);
                            }
                        }
                    }
                    else //lastHitObject == null, not hitting anything
                    {
                        //set clickdownobjects to null so that the up handler function knows the down click was on nothing
                        if (primaryClick == clickButton.bumper)
                            clickDownObject = null;
                        else
                            click_2_DownObject = null;
                    }
                }
            }
        }

        /// <summary>
        /// This function is a handler for button up events from the controller. It will change the selected object,
        /// call related select handlers, or end drags
        /// </summary>
        /// <param name="controllerID">See above function</param>
        /// <param name="button"></param>
        void ControllerButtonUpHandler(byte controllerID, MLInputControllerButton button)
        {
            //Controller verification, make sure the bumper is the one that was pressed
            if (_controller != null && _controller.Id == controllerID && button == MLInputControllerButton.Bumper)
            {
                UpdateEventData(eventData);
                //Only call these if not dragging
                if (!isDragging)
                {
                    //Call this if the bumper was released on an object(RayHit)
                    if (lastHitObject != null)
                    {
                        //If this is the primary clicker, do this(update selected object)
                        if (primaryClick == clickButton.bumper)
                        {
                            //get, check, and call handler
                            IMLPointerUpHandler upHandler = lastHitObject.GetComponent<IMLPointerUpHandler>();
                            if (upHandler != null)
                            {
                                upHandler.MLOnPointerUp(eventData);
                            }
                            //Check to see if the clickdown started and ended on the same object. If it did, consider click or select handlers
                            if (clickDownObject != null && lastHitObject == clickDownObject)
                            {
                                //If the release was quick enough, it is considered a click
                                if (Time.time - bumperTimer < clickWindow)
                                {
                                    //get, check, and call handler
                                    IMLPointerClickHandler clickHandler = lastHitObject.GetComponent<IMLPointerClickHandler>();
                                    if (clickHandler != null)
                                    {
                                        clickHandler.MLOnPointerClick(eventData);
                                    }
                                }
                                //If no object was currently selected, then call the select handler on the new hit object
                                if (lastSelectedObject == null)
                                {
                                    //get, check, and call handler, update selected object only if the hit object has a select handler, indicating it is selectable
                                    IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                                    if (selectHandler != null)
                                    {
                                        selectHandler.MLOnSelect(eventData);
                                        lastSelectedObject = lastHitObject;
                                        //This caches the reference to the update select handler if it exists
                                        IMLUpdateSelectedHandler updateSelectedHandler = lastHitObject.GetComponent<IMLUpdateSelectedHandler>();
                                        UpdateEventData(eventData);
                                    }
                                }
                                //If there was a previously selected object, call its deselect only if the newly hit object is selectable, has a select handler
                                else if (lastHitObject != lastSelectedObject)
                                {
                                    IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                                    if (selectHandler != null)
                                    {
                                        selectHandler.MLOnSelect(eventData);
                                        IMLDeselectHandler deselectHandler = lastSelectedObject.GetComponent<IMLDeselectHandler>();
                                        if (deselectHandler != null)
                                        {
                                            deselectHandler.MLOnDeselect(eventData);//checks for not null
                                        }
                                        //This caches the reference to the update select handler if it exists
                                        IMLUpdateSelectedHandler updateSelectedHandler = lastHitObject.GetComponent<IMLUpdateSelectedHandler>();
                                        lastSelectedObject = lastHitObject;
                                    }
                                }
                            }
                        }
                        else //bumper == click_2, secondary click
                        {
                            //get, check, and call handler
                            IMLPointer_2_UpHandler up_2_Handler = lastHitObject.GetComponent<IMLPointer_2_UpHandler>();
                            if (up_2_Handler != null)
                            {
                                up_2_Handler.MLOnPointer_2_Up(eventData);
                            }
                            //If click started and ended on the same object, and was within the time window, treat it as a click
                            if (click_2_DownObject != null && lastHitObject == click_2_DownObject && Time.time - bumperTimer < clickWindow)
                            {
                                //get, chack, and call handler
                                IMLPointer_2_ClickHandler click_2_Handler = lastHitObject.GetComponent<IMLPointer_2_ClickHandler>();
                                if (click_2_Handler != null)
                                {
                                    click_2_Handler.MLOnPointer_2_Click(eventData);
                                }
                            }
                        }
                    }
                    else //No Current Hit Object, so released button on empty space
                    {
                        //If bumper is the primary button(can change selection), and it also began the click on empty space, then it is deselecting
                        //Also check if there is a last selected object to now deselect
                        if (primaryClick == clickButton.bumper && clickDownObject == null && lastSelectedObject != null)
                        {
                            //get, check, and call handler
                            IMLDeselectHandler deselectHandler = lastSelectedObject.GetComponent<IMLDeselectHandler>();
                            if (deselectHandler != null)
                            {
                                deselectHandler.MLOnDeselect(eventData);
                            }
                            lastSelectedObject = null; //update selected object to nothing
                        }
                    }
                }
                //if dragging and the primary button is released, stop the drag
                else if (isDragging == true && primaryClick == clickButton.bumper)
                {
                    //get, chack, and call handler
                    IMLEndDragHandler endDragHandler = lastHitObject.GetComponent<IMLEndDragHandler>();
                    if (endDragHandler != null)
                    {
                        endDragHandler.MLOnEndDrag(eventData);
                    }
                    //Double check that the dragged object isn't already the selected object
                    if (lastHitObject != lastSelectedObject)
                    {
                        //Call select handler at the end of the drag, update selected object
                        IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                        if (selectHandler != null)
                        {
                            selectHandler.MLOnSelect(eventData);
                            lastSelectedObject = lastHitObject;
                        }
                    }
                    isDragging = false; //End the drag, because primary button was released
                    draggedObject = null;
                }
            }
        }
        #endregion //Button Handlers

        #region Trigger Handlers
        /// <summary>
        /// This function handles a trigger down event by determining what  events to call
        /// </summary>
        /// <param name="controllerID">see above</param>
        /// <param name="triggerValue"> float from 0 to 1 for how much the trigger was pressed at the time of the event call</param>
        void TriggerDownHandler(byte controllerID, float triggerValue)
        {
            if (_controller != null && _controller.Id == controllerID)
            {
                UpdateEventData(eventData);
                /* If this is the primary button
                 * Then it already started the drag, and shouldn't call any more button down events anyway. If it is
                 * the secondary button, then we aren't interested in any of its clicks during a drag, because even if it hits
                 * nothing, it has to call exit handlers*/
                if (!isDragging)
                {
                    //This checks if the button was pressed while pointing at an object
                    if (lastHitObject != null)
                    {
                        //Set the start time to test if when the trigger is released it was quick enough to be considered a click
                        triggerTimer = Time.time;
                        //If the trigger is the primary clicker, then execute this code
                        if (primaryClick == clickButton.trigger)
                        {
                            //This reference allows the system to check if a click was released on the same object it started on, a criteria for some events
                            clickDownObject = lastHitObject;
                            //Get, check, and call the down handler on the object
                            IMLPointerDownHandler downHandler = lastHitObject.GetComponent<IMLPointerDownHandler>();
                            if (downHandler != null)
                            {
                                downHandler.MLOnPointerDown(eventData);
                            }
                            //If you click on a draggable object, then you may want to start a drag
                            //So check if it has this handler
                            IMLInitializePotentialDragHandler potentialDragHandler = lastHitObject.GetComponent<IMLInitializePotentialDragHandler>();
                            if (potentialDragHandler != null)
                            {
                                potentialDragHandler.MLOnInitializePotentialDrag(eventData);
                                                                                                     //If the object has a potential drag initializer, then a drag must be actually started through criteria evaluated elsewhere
                            }
                            else //The object has no potentialDragInitializer, so just start the drag
                            {
                                //For an object to be considered draggable, it must implement the beginDrag interface, as well as the Drag
                                IMLBeginDragHandler beginDragHandler = lastHitObject.GetComponent<IMLBeginDragHandler>();
                                if (beginDragHandler != null)
                                {
                                    beginDragHandler.MLOnBeginDrag(eventData);
                                    isDragging = true;
                                    draggedObject = lastHitObject;
                                    //This assignment prevents the raycast from entering drag state without a prev hit object
                                    //_raycaster.PrevHitObject = lastHitObject;
                                    dragHandler = lastHitObject.GetComponent<IMLDragHandler>(); //cache reference to drag handler
                                                                                                //If the object about to be dragged has not all ready been selected, then do this
                                    if (lastSelectedObject != null && LastHitObject != lastSelectedObject)
                                    {
                                        //This is done so that the last selected object is no longer selected once a drag starts
                                        IMLDeselectHandler deselectHandler = lastSelectedObject?.GetComponent<IMLDeselectHandler>();
                                        if (deselectHandler != null)
                                        {
                                            deselectHandler.MLOnDeselect(eventData);
                                            lastSelectedObject = null;
                                        }
                                    }
                                }
                            }
                        }
                        else //trigger == click_2, not primary
                        {
                            click_2_DownObject = lastHitObject;//To be checked before select and click is called in up handler
                                                               //Get, check, and call handler:
                            IMLPointer_2_DownHandler down_2_Handler = lastHitObject.GetComponent<IMLPointer_2_DownHandler>();
                            if (down_2_Handler != null)
                            {
                                down_2_Handler.MLOnPointer_2_Down(eventData);
                            }
                        }
                    }
                    else //lastHitObject == null, not hitting anything
                    {
                        //reset clickdownobjects so that the up handler function knows the down click was on nothing
                        if (primaryClick == clickButton.trigger)
                            clickDownObject = null;
                        else
                            click_2_DownObject = null;
                    }
                }
            }
        }

        /// <summary>
        /// handles a trigger up event by sending events based on system logic
        /// </summary>
        /// <param name="controllerID">see above</param>
        /// <param name="triggerValue">a float from 0 to 1 for how much the trigger is pressed at the time of the event</param>
        void TriggerUpHandler(byte controllerID, float triggerValue)
        {
            if (_controller != null && _controller.Id == controllerID)
            {
                UpdateEventData(eventData);
                //Only call these if not dragging
                if (!isDragging)
                {
                    //Call this if the trigger was pressed on an object(RayHit)
                    if (lastHitObject != null)
                    {
                        //If the trigger is the primary clicker, do this(update selected object)
                        if (primaryClick == clickButton.trigger)
                        {
                            //get, check, and call handler
                            IMLPointerUpHandler upHandler = lastHitObject.GetComponent<IMLPointerUpHandler>();
                            if (upHandler != null)
                            {
                                upHandler.MLOnPointerUp(eventData);
                            }
                            //Check to see if the clickdown started and ended on the same object. If it did, consider click or select handlers
                            if (clickDownObject != null && lastHitObject == clickDownObject)
                            {
                                //If the release was quick enough, it is considered a click
                                if (Time.time - triggerTimer < clickWindow)
                                {
                                    //get, check, and call handler
                                    IMLPointerClickHandler clickHandler = lastHitObject.GetComponent<IMLPointerClickHandler>();
                                    if (clickHandler != null)
                                    {
                                        clickHandler.MLOnPointerClick(eventData);
                                    }
                                }

                                //If no object was currently selected, then call the select handler on the new hit object
                                if (lastSelectedObject == null)
                                {
                                    //get, check, and call handler, update selected object only if the hit object has a select handler, indicating it is selectable
                                    IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                                    if (selectHandler != null)
                                    {
                                        selectHandler.MLOnSelect(eventData);
                                        lastSelectedObject = lastHitObject;
                                        //This caches the reference to the update select handler if it exists
                                        IMLUpdateSelectedHandler updateSelectedHandler = lastHitObject.GetComponent<IMLUpdateSelectedHandler>();
                                    }
                                }
                                //If there was a previously selected object, call its deselect only if the newly hit object is selectable,ie. has a select handler
                                else if (lastHitObject != lastSelectedObject)
                                {
                                    IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                                    if (selectHandler != null)
                                    {
                                        IMLDeselectHandler deselectHandler = lastSelectedObject.GetComponent<IMLDeselectHandler>();
                                        if (deselectHandler != null)
                                        {
                                            deselectHandler.MLOnDeselect(eventData);//checks for not null
                                        }
                                        //This caches the reference to the update select handler if it exists
                                        IMLUpdateSelectedHandler updateSelectedHandler = lastHitObject.GetComponent<IMLUpdateSelectedHandler>();
                                        selectHandler.MLOnSelect(eventData);
                                        lastSelectedObject = lastHitObject;
                                    }
                                }
                            }
                        }
                        else //trigger == click_2, secondary click
                        {
                            //get, check, and call handler
                            IMLPointer_2_UpHandler up_2_Handler = lastHitObject.GetComponent<IMLPointer_2_UpHandler>();
                            if (up_2_Handler != null)
                            {
                                up_2_Handler.MLOnPointer_2_Up(eventData);
                            }
                            //If click started and ended on the same object, and was within the time window, treat it as a click
                            if (click_2_DownObject != null && lastHitObject == click_2_DownObject && Time.time - triggerTimer < clickWindow)
                            {
                                //get, chack, and call handler
                                IMLPointer_2_ClickHandler click_2_Handler = lastHitObject.GetComponent<IMLPointer_2_ClickHandler>();
                                if (click_2_Handler != null)
                                {
                                    click_2_Handler.MLOnPointer_2_Click(eventData);
                                }
                            }
                        }
                    }
                    else //No Current Hit Object, so released button on empty space
                    {
                        //If trigger is the primary button(can change selection), and it also began the click on empty space, then it is deselecting
                        //Also check if there is a last selected object to now deselect
                        if (primaryClick == clickButton.trigger && clickDownObject == null && lastSelectedObject != null)
                        {
                            //get, check, and call handler
                            IMLDeselectHandler deselectHandler = lastSelectedObject.GetComponent<IMLDeselectHandler>();
                            if (deselectHandler != null)
                            {
                                deselectHandler.MLOnDeselect(eventData);
                            }
                            lastSelectedObject = null; //update selected object to nothing
                        }
                    }
                }
                //This makes sure we aredragging, regardless of a UI hit, because we need to be able to end a drag at any time
                else if (isDragging == true)
                {
                    //If he bumper can change selections, is primary button
                    if (primaryClick == clickButton.trigger)
                    {
                        //get, chack, and call handler
                        IMLEndDragHandler endDragHandler = lastHitObject.GetComponent<IMLEndDragHandler>();
                        if (endDragHandler != null)
                        {
                            endDragHandler.MLOnEndDrag(eventData);
                        }
                        //Double check that the dragged object isn't already the selected object
                        if (lastHitObject != lastSelectedObject)
                        {
                            //Call select handler at the end of the drag, update selected object
                            IMLSelectHandler selectHandler = lastHitObject.GetComponent<IMLSelectHandler>();
                            if (selectHandler != null)
                            {
                                selectHandler.MLOnSelect(eventData);
                                lastSelectedObject = lastHitObject;
                            }
                        }
                        isDragging = false; //End the drag, because primary button was released
                        draggedObject = null;
                    }
                }
            }
        }
        #endregion //Trigger Handlers
    }
}