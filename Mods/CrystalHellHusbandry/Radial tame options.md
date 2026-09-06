Radial tame options

    [What needs to happen]

        - May need to make a seperate radial controller
            - it would be very difficult to make a custom controller due to how many times playermovecontroller calls the XUiC_Radial methods
            - it would be better to harmony patch methods in order to achieve what i want.

            [To make my own radial controller]
                - need to harmony patch the playermovecontroller to include looking at a tamed animal and link it to the custom radial controller methods

        - Need to get a raycast for the entity you're looking at
            - need to make sure the entity is tamed
            - need to make sure the entity is owned by the player looking at it
        - when the player is looking at an owned tame, they can hold "E" and open a radial command window
            - 

    [How does the radial window work?]

        - on the player controller update function, the player recieves objects through a raycast. 
            - If the object is an entity, it calls the XUIC_Radial SetCurrentEntityData method.
                - this method is passed (WorldBase, Entity, ITileEntity, EntityAlive) all from the playercontroller update method



    - find out how a dead entity with a loot list maps back to a radial menu
    