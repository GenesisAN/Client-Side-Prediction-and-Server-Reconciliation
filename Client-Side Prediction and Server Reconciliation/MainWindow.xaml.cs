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
using System.Windows.Threading;

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

            this.Loaded += MainWindow_Loaded;
        }

        Server server;
        Client player1;
        Client player2;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            server = new Server();
            server.uiSliders = new Slider[] { s21, s22 };
            player1 = new Client();
            player1.uiSliders = new Slider[] { s11, s12 };
            player1.client_side_prediction = true;
            player1.entity_interpolation = true;
            player1.server_reconciliation = true;
            player2 = new Client();
            player2.uiSliders = new Slider[] { s31, s32 };
            player2.client_side_prediction = true;
            player2.entity_interpolation = true;
            player2.server_reconciliation = true;
            // Connect the clients to the server.
            server.connect(player1);
            server.connect(player2);

            // Read initial parameters from the UI.
            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            bool flag = false;
            if (e.Key == Key.Right)
            {
                player1.key_right = flag;
            }
            else if (e.Key == Key.Left)
            {
                player1.key_left = flag;
            }
            else if (e.Key == Key.D)
            {
                player2.key_right = flag;
            }
            else if (e.Key == Key.A)
            {
                player2.key_left = flag;
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            bool flag = true;
            if (e.Key == Key.Right)
            {
                player1.key_right = flag;
            }
            else if (e.Key == Key.Left)
            {
                player1.key_left = flag;
            }
            else if (e.Key == Key.D)
            {
                player2.key_right = flag;
            }
            else if (e.Key == Key.A)
            {
                player2.key_left = flag;
            }
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
            public double speed = 15;
            public List<Position> position_buffer = new List<Position>();
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
                DateTime recv_ts { get; set; }
            }

            public class ClientMessage : IMessage
            {
                public DateTime recv_ts { get; set; }
                public Client.Input input;
            }

            public class ServerMessage : IMessage
            {
                public DateTime recv_ts { get; set; }
                public Server.State[] states;
            }

            public List<IMessage> messages = new List<IMessage>();

            public void send(int lag_ms, IMessage message)
            {
                lock (messages)
                {
                    message.recv_ts = DateTime.Now.AddMilliseconds(lag_ms);
                    messages.Add(message);
                }
                
            }

            public IMessage receive()
            {
                lock (messages)
                {
                    DateTime now = DateTime.Now;
                    for (int i = 0; i < messages.Count(); i++)
                    {
                        IMessage message = messages[i];
                        if (message.recv_ts <= now)
                        {
                            messages.RemoveAt(0);
                            return message;
                        }
                    }
                    return null;
                }
                    
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
            public int lag;

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

            public Slider[] uiSliders;

            public Client()
            {
                entities = new Dictionary<int, Entity>();
                key_left = false;
                key_right = false;
                network = new LagNetwork();
                server = null;
                lag = 100;
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

                if (entity_id == -1)
                    return;  // Not connected yet.

                processInputs();

                if (entity_interpolation)
                    interpolateEntities();

                // TODO : Render the World.
                //renderWorld(this.canvas, this.entities);

                for (int i = 0; i < uiSliders.Length; i++)
                    if (entities.ContainsKey(i))
                        uiSliders[i].Dispatcher.Invoke(() =>
                        {
                            uiSliders[i].Value = entities[i].x;
                        });
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
                server.network.send(lag, new LagNetwork.ClientMessage() { input = input });
                

                // Do client-side prediction.
                if (client_side_prediction)
                {
                    entities[entity_id].applyInput(input);
                }

                // Save this input for later reconciliation.
                pending_inputs.Add(input);
            }

            public void processServerMessages()
            {
                while (true)
                {
                    LagNetwork.IMessage message = network.receive();
                    if (message == null)
                    {
                        break;
                    }

                    Server.State[] states = ((LagNetwork.ServerMessage)message).states;

                    // World state is a list of entity states.
                    for (int i = 0; i < states.Length; i++)
                    {
                        Server.State state = states[i];

                        // If this is the first time we see this entity, create a local representation.
                        if (!entities.ContainsKey(state.entity_id))
                        {
                            Entity newEntity = new Entity();
                            newEntity.entity_id = state.entity_id;
                            entities[state.entity_id] = newEntity;
                        }

                        Entity entity = entities[state.entity_id];

                        if (state.entity_id == entity_id)
                        {
                            // Received the authoritative position of this client's entity.
                            entity.x = state.position;

                            if (server_reconciliation)
                            {
                                // Server Reconciliation. Re-apply all the inputs not yet processed by
                                // the server.
                                var j = 0;
                                while (j < this.pending_inputs.Count)
                                {
                                    Input input = pending_inputs[j];
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
                                entity.x = state.position;
                            }
                            else
                            {
                                // Add it to the position buffer.
                                DateTime timestamp = DateTime.Now;
                                entity.position_buffer.Add(new Entity.Position(timestamp, state.position));
                            }
                        }
                    }
                }
            }

            public void interpolateEntities()
            {
                // Compute render timestamp.
                DateTime now = DateTime.Now;
                DateTime render_timestamp = now.AddMilliseconds(-(1000.0 / server.update_rate));

                foreach (Entity entity in entities.Values)
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

        public class Server
        {
            public class State
            {
                public int entity_id;
                public double position;
                public long last_processed_input;
            }

            public List<Client> clients;
            public List<Entity> entities;

            // 为每个客户端最后处理的输入。
            public List<long> last_processed_input;

            //模拟网络连接。
            public LagNetwork network;

            double[] spawn_points = new double[] { -20, 20 };
            public Slider[] uiSliders;

            public int update_rate;
            // UI.
            //this.canvas = canvas;
            //this.status = status;

            public Server()
            {
                clients = new List<Client>();
                entities = new List<Entity>();
                last_processed_input = new List<long>();
                network = new LagNetwork();
                setUpdateRate(10);
            }

            public void connect(Client client)
            {
                // Give the Client enough data to identify itself.
                client.server = this;
                client.entity_id = clients.Count;
                clients.Add(client);

                // Create a new Entity for this Client.
                Entity entity = new Entity();
                entities.Add(entity);
                entity.entity_id = client.entity_id;

                last_processed_input.Add(DateTime.Now.Millisecond);

                // Set the initial state of the Entity (e.g. spawn point)
                entity.x = spawn_points[client.entity_id];
            }

            Thread updateThread;
            public void setUpdateRate(int hz)
            {
                update_rate = hz;

                if (updateThread != null)
                    updateThread.Abort();
                updateThread = new Thread(delegate ()
                {
                    while (true)
                    {
                        Thread.Sleep(1000 / update_rate);
                        update();                     
                    }
                });
                updateThread.Start();
            }

            public void update()
            {
                processInputs();
                sendWorldState();

                // TODO :
                //renderWorld(this.canvas, this.entities);
                for (int i = 0; i < uiSliders.Length; i++)
                    if (entities.Count > i)
                        uiSliders[i].Dispatcher.Invoke(() =>
                        {
                            uiSliders[i].Value = entities[i].x;
                        });
            }

            public bool validateInput(Client.Input input)
            {
                if (Math.Abs(input.press_time.TotalMilliseconds) > 1 / 40)
                    return false;
                return true;
            }

            public void processInputs()
            {
                // Process all pending messages from clients.
                while (true)
                {
                    LagNetwork.IMessage message = this.network.receive();
                    if (message == null)
                        break;
                    Client.Input input = ((LagNetwork.ClientMessage)message).input;

                    // Update the state of the entity, based on its input.
                    // We just ignore inputs that don't look valid; this is what prevents clients from cheating.
                    if (true || validateInput(input))
                    {
                        int id = input.entity_id;
                        entities[id].applyInput(input);
                        last_processed_input[id] = input.input_sequence_number;
                    }

                }
            }

            public void sendWorldState()
            {
                // Gather the state of the world. In a real app, state could be filtered to avoid leaking data
                // (e.g. position of invisible enemies).
                List<State> world_state = new List<State>();
                int num_clients = clients.Count;

                for (var i = 0; i < num_clients; i++)
                {
                    Entity entity = entities[i];
                    world_state.Add(new State() { entity_id = entity.entity_id, position = entity.x, last_processed_input = last_processed_input[i] });
                }

                // Broadcast the state to all the clients.
                for (int i = 0; i < num_clients; i++)
                {
                    Client client = clients[i];
                    client.network.send(client.lag, new LagNetwork.ServerMessage() { states = world_state.ToArray() });
                }

            }

        }


    }
}
