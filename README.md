# DriftKing-DriftController

This is the controller I wrote for the drifting on driftking.

# About DriftKing:

Driftking is an online MOBA-esque game where you control a car, and you can drift to cast spells. When you drift, you generate power that you can then unleash on your enemies in the form of attacking or hindering spells

You can learn more and watch the trailer at https://www.duartemaia.com/#/driftking/

# About this Controller

This is the controller which detects and handles the physics when the player goes into a drift. It is a tri-state machine that I made with the objective of not only meeting new players' expectations of the controls coming into a driving game, but also a handling that could allow skill progression and free up more complex possibilities for gameplay as the player gets more experienced.

The states of drifting are: NOT DRIFTING, PRE-DRIFTING, DRIFTING. The NOT DRIFTING state simply allows the car to run on its normal physics. In order to accomodate expectation from the player, a drift would have to feel like a powerful way to cut corners. Therefore, I added the PRE-DRIFTING state where the slippage on the tires is maximum, allowing players to quickly snap into a sideways position without much alteration to their trajectory. 

By evaluating the dot product between the car's forward vector and its velocity, the player enters the DRIFTING state if a certain threshold of sideways momentum is met

Each of the states has its own set of applied forces that make the car behave in the way we designed it to. These forces mostly depend on current speed, the forward-to-velocity dot product, and direct input
