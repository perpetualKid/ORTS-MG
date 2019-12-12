namespace Orts.Simulation.Commanding
{
    /// <summary>
     /// This Command Pattern allows requests to be encapsulated as objects (http://sourcemaking.com/design_patterns/command).
     /// The pattern provides many advantages, but it allows OR to record the commands and then to save them when the user presses F2.
     /// The commands can later be read from file and replayed.
     /// Writing and reading is done using the .NET binary serialization which is quick to code. (For an editable version, JSON has
     /// been successfully explored.)
     /// 
     /// Immediate commands (e.g. sound horn) are straightforward but continuous commands (e.g. apply train brake) are not. 
     /// OR aims for commands which can be repeated accurately and possibly on a range of hardware. Continuous commands therefore
     /// have a target value which is recorded once the key is released. OR creates an immediate command as soon as the user 
     /// presses the key, but OR creates the continuous command once the user releases the key and the target is known. 
     /// 
     /// All commands record the time when the command is created, but a continuous command backdates the time to when the key
     /// was pressed.
     /// 
     /// Each command class has a Receiver property and calls methods on the Receiver to execute the command.
     /// This property is static for 2 reasons:
     /// - so all command objects of the same class will share the same Receiver object;
     /// - so when a command is serialized to and deserialised from file, its Receiver does not have to be saved 
     ///   (which would be impractical) but is automatically available to commands which have been re-created from file.
     /// 
     /// Before each command class is used, this Receiver must be assigned, e.g.
     ///   ReverserCommand.Receiver = (MSTSLocomotive)PlayerLocomotive;
     /// 
     /// </summary>

    public interface ICommand
    {
        /// <summary>
        /// The time when the command was issued (compatible with Simlator.ClockTime).
        /// </summary>
        double Time { get; set; }

        /// <summary>
        /// Call the Receiver to repeat the Command.
        /// Each class of command shares a single object, the Receiver, and the command executes by
        /// call methods of the Receiver.
        /// </summary>
        void Redo();

        /// <summary>
        /// Print the content of the command.
        /// </summary>
        void Report();
    }
}
