using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;

namespace Client_Side_Prediction_and_Server_Reconciliation
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
        }
        /// <summary>
        /// 实体
        /// </summary>
        public class Entity
        {
            public class Position
            {
                public DateTime timestamp;
                public double position;

                public Position(DateTime timestamp, double position)
                {
                    this.timestamp = timestamp;
                    this.position = position;
                }
            }

            public int entity_id;
            public double x = 0;
            public double speed = 2;
            public List<Position> position_buffer;
            public Slider ui_slider;                    // Appended

            public void applyInput(Client.Input input)
            {
                x += input.press_time.TotalSeconds * speed;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class LagNetwork
        {
            public interface IMessage
            {
                public DateTime recv_ts;
            }

            public class ClientMessage : IMessage
            {
                public DateTime recv_ts;
                public Client.Input input;
            }

            public class ServerMessage : IMessage
            {
                public DateTime recv_ts;
                public class State
                {
                    public int entity_id;
                    public double position;
                    public Client.Input last_processed_input;
                }
                public State[] states;
            }

            public List<IMessage> messages;

            public void send(float lag_ms, Message message)
            {
                messages.Add(message);
            }

            public IMessage receive()
            {
                DateTime now = DateTime.Now;
                for (int i = 0; i < messages.Count(); i++)
                {
                    IMessage message = messages[i];
                    if (message.recv_ts <= now)
                    {
                        messages.RemoveAt(0);
                        return message.payload;
                    }
                }
                return null;
            }

        }


        public class Client
        {
            public class Input
            {
                public TimeSpan press_time;
                public long input_sequence_number;
                public int entity_id;
            }
            /// <summary>
            /// 
            /// </summary>
            // Local representation of the entities.
            public Dictionary<int, Entity> entities;

            // Input state.
            public bool key_left;
            public bool key_right;

            // Simulated network connection.
            public LagNetwork network;
            public Server server;
            public int lag = 0;

            // Unique ID of our entity. Assigned by Server on connection.
            public int entity_id;

            // Data needed for reconciliation.
            public bool client_side_prediction;
            public bool server_reconciliation;
            public long input_sequence_number;
            public List<Input> pending_inputs;

            // Entity interpolation toggle.
            public bool entity_interpolation;

            // Removed, See ui_slider in the Entity
            // UI.
            //public int canvas = canvas;
            //public int status = status;

            public Client()
            {
                entities = new Dictionary<int, Entity>();
                key_left = false;
                key_right = false;
                network = new LagNetwork();
                server = null;
                lag = 0;
                entity_id = -1;
                client_side_prediction = false;
                server_reconciliation = false;
                input_sequence_number = 0;
                pending_inputs = new List<Input>();
                entity_interpolation = true;

                setUpdateRate(50);
            }

            Thread update_thread;
            int update_rate;
            public void setUpdateRate(int hz)
            {
                update_rate = hz;

                // TODO : Update thread
                if (update_thread != null)
                    update_thread.Abort();
                update_thread = new Thread(delegate ()
                {
                    while (true)
                    {
                        Thread.Sleep(1000 / update_rate);
                        update();
                    }
                });
                update_thread.Start();
            }

            public void update()
            {
                processServerMessages();

                if (entity_id == null)
                    return;  // Not connected yet.

                processInputs();

                if (entity_interpolation)
                    interpolateEntities();

                // TODO : Render the World.
                //renderWorld(this.canvas, this.entities);
            }

            DateTime last_ts = DateTime.Now;

            public void processInputs()
            {
                DateTime now_ts = DateTime.Now;
                TimeSpan dt_sec = now_ts - last_ts;
                last_ts = now_ts;

                Input input = new Input();
                if (key_right)
                    input.press_time = dt_sec;
                else if (key_left)
                    input.press_time = -dt_sec;
                else
                    return;             // Nothing interesting happened.

                // Send the input to the server.
                input.input_sequence_number = input_sequence_number++;
                input.entity_id = entity_id;
                server.network.send(lag, new Input[] { input });

                // Do client-side prediction.
                if (client_side_prediction)
                {
                    entities[entity_id].applyInput(input);
                }

                // Save this input for later reconciliation.
                pending_inputs.push(input);
            }

            public void processServerMessages()
            {
                while (true)
                {
                    IMessage inputs = network.receive();
                    if (inputs == null)
                    {
                        break;
                    }

                    // World state is a list of entity states.
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        Input input = inputs[i];

                        // If this is the first time we see this entity, create a local representation.
                        if (!entities.ContainsKey(input.entity_id))
                        {
                            Entity newEntity = new Entity();
                            newEntity.entity_id = input.entity_id;
                            entities[input.entity_id] = newEntity;
                        }

                        Entity entity = entities[input.entity_id];

                        if (input.entity_id == entity_id)
                        {
                            // Received the authoritative position of this client's entity.
                            entity.x = input.position;

                            if (server_reconciliation)
                            {
                                // Server Reconciliation. Re-apply all the inputs not yet processed by
                                // the server.
                                var j = 0;
                                while (j < this.pending_inputs.Count)
                                {
                                    Input input = this.pending_inputs[j];
                                    if (input.input_sequence_number <= state.last_processed_input)
                                    {
                                        // Already processed. Its effect is already taken into account into the world update
                                        // we just got, so we can drop it.
                                        this.pending_inputs.RemoveAt(j);
                                    }
                                    else
                                    {
                                        // Not processed by the server yet. Re-apply it.
                                        entity.applyInput(input);
                                        j++;
                                    }
                                }
                            }
                            else
                            {
                                // Reconciliation is disabled, so drop all the saved inputs.
                                pending_inputs.Clear();
                            }
                        }
                        else
                        {
                            // Received the position of an entity other than this client's.

                            if (!entity_interpolation)
                            {
                                // Entity interpolation is disabled - just accept the server's position.
                                entity.x = input.position;
                            }
                            else
                            {
                                // Add it to the position buffer.
                                DateTime timestamp = DateTime.Now;
                                entity.position_buffer.Add(new Entity.Position(timestamp, input.position));
                            }
                        }
                    }
                }
            }

            public void interpolateEntities()
            {
                // Compute render timestamp.
                DateTime now = DateTime.Now;
                DateTime render_timestamp = now.AddSeconds(-(1000.0 / server.update_rate));

                foreach (Entity entity in entities)
                {
                    // No point in interpolating this client's entity.
                    if (entity.entity_id == entity_id)
                    {
                        continue;
                    }

                    // Find the two authoritative positions surrounding the rendering timestamp.
                    List<Entity.Position> buffer = entity.position_buffer;

                    // Drop older positions.
                    while (buffer.Count >= 2 && buffer[1].timestamp <= render_timestamp)
                    {
                        buffer.RemoveAt(0);
                    }

                    // Interpolate between the two surrounding authoritative positions.
                    if (buffer.Count >= 2 && buffer[0].timestamp <= render_timestamp && render_timestamp <= buffer[1].timestamp)
                    {
                        double x0 = buffer[0].position;
                        double x1 = buffer[1].position;
                        DateTime t0 = buffer[0].timestamp;
                        DateTime t1 = buffer[1].timestamp;

                        entity.x = x0 + (x1 - x0) * ((render_timestamp - t0).TotalMilliseconds / (t1 - t0).TotalMilliseconds);
                    }
                }
            }
        }
    }


    public class Server
    {
        public List<Client> clients;
        public List<Client> entities;

        // 为每个客户端最后处理的输入。
        this.last_processed_input = [];

        //模拟网络连接。
        public LagNetwork network = new LagNetwork();

        // UI.
        this.canvas = canvas;
        this.status = status;

        public Server()
        {
                




        }


    }



}
