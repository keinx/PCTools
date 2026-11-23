// XAML 常量集中在这里，PCTools.cs 保留可执行逻辑。

namespace PCTools
{
    // ------------------------------------------------------------------ XAML
    // XAML 常量集中在这里，PCTools.cs 保留可执行逻辑。
    // 单文件即可，不再拆 partial：第二个分片早已不存在
    internal static class Views
    {
        // 这些 XAML 只在运行时传给 XamlReader.Parse。用 static readonly 而不是 const：
        // const 会被编译器在常量表和每处引用各存一份 UTF-16 副本，单这一项就多出约 260KB。
        // 细滚动条：主窗口与所有弹窗共用。原来只写在 Views.Main 的 Grid.Resources 里，
        // 弹窗的视觉树拿不到，就退回系统默认的 17px 带箭头样式
        internal static readonly string ScrollSkin = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='ThinThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='tb' CornerRadius='3' Margin='3,3' Background='{DynamicResource ScrollThumb}'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='tb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
            </Trigger>
            <Trigger Property='IsDragging' Value='True'>
              <Setter TargetName='tb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='12'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='true' Focusable='False'
                   Orientation='{TemplateBinding Orientation}'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'
                              Opacity='0' OverridesDefaultStyle='True'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource ThinThumb}'/>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'
                              Opacity='0' OverridesDefaultStyle='True'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Orientation' Value='Horizontal'>
              <Setter Property='Width' Value='Auto'/>
              <Setter Property='Height' Value='12'/>
              <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
        // All XAML attributes use single quotes so the C# verbatim string needs no escaping.
        internal static readonly string Main = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
      x:Name='Root' Background='{DynamicResource AppBg}'>
  <Grid.Resources>
    <!-- 工具条图标几何串集中为资源：四个页面的同名按钮不再各存一份 -->
    <PathGeometry x:Key='IconCheck' Figures='M 1,5.5 L 4.2,8.8 L 10.5,2'/>
    <PathGeometry x:Key='IconDisable' Figures='M 5.75,1 A 4.75,4.75 0 1 0 5.75,10.5 A 4.75,4.75 0 1 0 5.75,1 M 2.4,9 L 9.1,2.3'/>
    <PathGeometry x:Key='IconRefresh' Figures='M 10,5.75 A 4.4,4.4 0 1 1 5.75,1.2 M 5.75,1.2 L 8.3,1.2 M 5.75,1.2 L 5.75,3.7'/>
    <PathGeometry x:Key='IconSparkle' Figures='M 5.75,0.8 L 7,4.5 L 10.7,5.75 L 7,7 L 5.75,10.7 L 4.5,7 L 0.8,5.75 L 4.5,4.5 Z'/>
    <PathGeometry x:Key='IconPlay' Figures='M 2.8,1.8 L 10,5.75 L 2.8,9.7 Z'/>
    <PathGeometry x:Key='IconStop' Figures='M 2.2,2.2 L 9.3,2.2 L 9.3,9.3 L 2.2,9.3 Z'/>
    <Style x:Key='TextCell' TargetType='TextBlock'>
      <Setter Property='TextTrimming' Value='CharacterEllipsis'/>
      <Setter Property='VerticalAlignment' Value='Center'/>
      <Setter Property='ToolTip' Value='{Binding RelativeSource={RelativeSource Self}, Path=Text}'/>
    </Style>
    <Style x:Key='SubCell' TargetType='TextBlock' BasedOn='{StaticResource TextCell}'>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='FontSize' Value='11.5'/>
    </Style>
    <Style x:Key='CmdBtn' TargetType='Button'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Height' Value='32'/>
      <Setter Property='Padding' Value='11,0'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='5' Background='{TemplateBinding Background}'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 命令栏按钮的统一外观：图标取 Tag（Geometry），文本取 Content -->
    <Style x:Key='CmdBtnIcon' TargetType='Button' BasedOn='{StaticResource CmdBtn}'>
      <Setter Property='ContentTemplate'>
        <Setter.Value>
          <DataTemplate>
            <StackPanel Orientation='Horizontal'>
              <Path Width='11' Height='11' Stretch='Uniform'
                    Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center'
                    Data='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Tag}'/>
              <TextBlock Text='{Binding}' VerticalAlignment='Center'/>
            </StackPanel>
          </DataTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtn' TargetType='Button'>
      <Setter Property='Width' Value='46'/>
<Setter Property='Height' Value='48'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource WinBtnHover}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource WinBtnPress}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtnSmall' TargetType='Button'>
      <Setter Property='Width' Value='36'/>
      <Setter Property='Height' Value='32'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='5' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtnClose' TargetType='Button' BasedOn='{StaticResource WinBtn}'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource CloseHover}'/>
                <Setter Property='Foreground' Value='#FFFFFF'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='NavItem' TargetType='ListBoxItem'>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ListBoxItem'>
            <Grid Margin='6,1'>
              <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='10,9'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                  VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='ind' Width='3' Height='18' CornerRadius='1.5' HorizontalAlignment='Left'
                      VerticalAlignment='Center' Background='Transparent' Margin='2,0,0,0'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsSelected' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                <Setter TargetName='ind' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='NavButton' TargetType='Button'>
      <Setter Property='Padding' Value='10,9'/>
      <Setter Property='Margin' Value='6,1'/>
      <Setter Property='HorizontalAlignment' Value='Stretch'/>
      <Setter Property='HorizontalContentAlignment' Value='Left'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Grid Margin='6,1'>
              <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                  VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='ind' Width='3' Height='18' CornerRadius='1.5' HorizontalAlignment='Left'
                      VerticalAlignment='Center' Background='Transparent' Margin='2,0,0,0'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
              <Trigger Property='Tag' Value='on'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                <Setter TargetName='ind' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='SearchBox' TargetType='TextBox'>
      <Setter Property='Height' Value='32'/>
      <!-- Height 显式 + VerticalAlignment=Center：标题栏变高时框仍在行内垂直居中 -->
      <Setter Property='VerticalAlignment' Value='Center'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border x:Name='bd' CornerRadius='6' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='10,0'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>
                <Path Grid.Column='0' Width='13' Height='13' Margin='0,0,8,0' Stretch='Uniform'
                      Stroke='{DynamicResource TextSecondary}' StrokeThickness='1.4'
                      Data='M 5,1 A 4,4 0 1 0 5,9 A 4,4 0 1 0 5,1 M 8,8 L 12,12'/>
                <ScrollViewer Grid.Column='1' x:Name='PART_ContentHost' VerticalAlignment='Center'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsFocused' Value='True'>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGrid'>
      <Setter Property='Background' Value='{DynamicResource ContentBg}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='RowHeight' Value='38'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='RowBackground' Value='Transparent'/>
      <Setter Property='AlternatingRowBackground' Value='Transparent'/>
      <Setter Property='GridLinesVisibility' Value='None'/>
      <Setter Property='HeadersVisibility' Value='Column'/>
      <Setter Property='CanUserAddRows' Value='False'/>
      <Setter Property='CanUserDeleteRows' Value='False'/>
      <Setter Property='CanUserResizeRows' Value='False'/>
      <Setter Property='CanUserSortColumns' Value='True'/>
      <Setter Property='CanUserResizeColumns' Value='True'/>
      <Setter Property='IsReadOnly' Value='True'/>
      <Setter Property='AutoGenerateColumns' Value='False'/>
      <Setter Property='SelectionMode' Value='Extended'/>
      <Setter Property='SelectionUnit' Value='FullRow'/>
      <Setter Property='EnableRowVirtualization' Value='True'/>
      <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Auto'/>
    </Style>
    <Style x:Key='ColGripper' TargetType='Thumb'>
      <Setter Property='Width' Value='7'/>
      <Setter Property='Cursor' Value='SizeWE'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Thumb'>
            <Border Background='Transparent'/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridColumnHeader'>
      <Setter Property='Background' Value='{DynamicResource HeaderBg}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='FontSize' Value='11.5'/>
<Setter Property='Height' Value='36'/>
      <Setter Property='Padding' Value='14,0'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='HorizontalContentAlignment' Value='Left'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='DataGridColumnHeader'>
            <Border x:Name='hd' Background='{TemplateBinding Background}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,0,0,1.34'
                    SnapsToDevicePixels='True'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter Grid.Column='0' Margin='{TemplateBinding Padding}'
                                  VerticalAlignment='Center'
                                  HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
                <Path x:Name='arrow' Grid.Column='1' Width='8' Height='4.5' Stretch='Fill'
                      Margin='0,0,4,0' VerticalAlignment='Center'
                      Fill='{DynamicResource Accent}' Visibility='Collapsed'
                      Data='M 0,4.5 L 4,0 L 8,4.5 Z'/>
                <Thumb x:Name='PART_LeftHeaderGripper' Style='{StaticResource ColGripper}'
                       HorizontalAlignment='Left'/>
                <Thumb x:Name='PART_RightHeaderGripper' Style='{StaticResource ColGripper}'
                       HorizontalAlignment='Right'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='hd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='SortDirection' Value='Ascending'>
                <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
              </Trigger>
              <Trigger Property='SortDirection' Value='Descending'>
                <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
                <Setter TargetName='arrow' Property='LayoutTransform'>
                  <Setter.Value>
                    <ScaleTransform ScaleY='-1'/>
                  </Setter.Value>
                </Setter>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridCell'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='Padding' Value='14,0'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <!-- 默认模板不使用 Padding，这里显式绑到 ContentPresenter，保证左右留白 -->
          <!-- BorderThickness 必须为 0：没有 BorderBrush 的边框不显示，却照样吃掉 2.68 DIP 内容宽度 -->
          <ControlTemplate TargetType='DataGridCell'>
            <Border Background='{TemplateBinding Background}' BorderThickness='0'
                    SnapsToDevicePixels='True'>
              <ContentPresenter Margin='{TemplateBinding Padding}'
                                VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                                SnapsToDevicePixels='True'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridRow'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='DataGridRow'>
            <Grid Background='Transparent'>
              <Border x:Name='hl' Background='Transparent'/>
              <DataGridCellsPresenter ItemsPanel='{TemplateBinding ItemsPanel}' VerticalAlignment='Center'
                                      SnapsToDevicePixels='True'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='hl' Property='Background' Value='{DynamicResource RowHoverBg}'/>
              </Trigger>
              <Trigger Property='IsSelected' Value='True'>
                <Setter TargetName='hl' Property='Background' Value='{DynamicResource RowSelectedBg}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 列表筛选复选框：整块胶囊都可点击（原来只有 14px 方框与文字可命中），
         勾选、悬停、禁用三种状态都有明显外观差异 -->
    <Style x:Key='ChkBox' TargetType='CheckBox'>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='CheckBox'>
            <Border x:Name='chip' CornerRadius='6' Background='{DynamicResource BtnBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34'
                    SnapsToDevicePixels='True' Padding='6,3' MinHeight='24'>
              <StackPanel Orientation='Horizontal'>
                <Border x:Name='box' Width='16' Height='16' CornerRadius='4'
                        Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource CheckBorder}'
                        BorderThickness='1.34' VerticalAlignment='Center'
                        SnapsToDevicePixels='True' RenderOptions.EdgeMode='Aliased'>
                  <Path x:Name='tick' Width='9' Height='9' Stretch='Uniform' Stroke='#FFFFFF'
                        StrokeThickness='1.9' Visibility='Collapsed' Data='M 1,4.4 L 3.4,6.8 L 8,2'/>
                </Border>
                <ContentPresenter Margin='7,0,0,0' VerticalAlignment='Center'/>
              </StackPanel>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='chip' Property='Background' Value='{DynamicResource HoverBg}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='chip' Property='Background' Value='{DynamicResource SelectionBg}'/>
                <Setter TargetName='chip' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
                <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
                <Setter Property='FontWeight' Value='SemiBold'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='chip' Property='Opacity' Value='0.45'/>
                <Setter Property='Cursor' Value='Arrow'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='StatusCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding StatusBgBrush}'>
        <TextBlock Text='{Binding Status}' Foreground='{Binding StatusBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='BackupCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding BackupBgBrush}'>
        <TextBlock Text='{Binding BackupText}' Foreground='{Binding BackupBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='AiAdviceCell'>
      <Border x:Name='aiBd' CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding AiAdviceBgBrush}' ToolTip='云端 AI 给出的建议，点击右上角「更新AI建议」刷新'>
        <TextBlock Text='{Binding AiAdvice}' Foreground='{Binding AiAdviceBrush}' FontSize='11.5'/>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding AiAdvice}' Value=''>
          <Setter TargetName='aiBd' Property='Visibility' Value='Collapsed'/>
        </DataTrigger>
        <DataTrigger Binding='{Binding AiAdvice}' Value='{x:Null}'>
          <Setter TargetName='aiBd' Property='Visibility' Value='Collapsed'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>
    <DataTemplate x:Key='StartTypeCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding StartTypeBgBrush}'>
        <TextBlock Text='{Binding StartType}' Foreground='{Binding StartTypeBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='StartupNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 2.5,5 A 1.5,1.5 0 0 1 4,3.5 L 12,3.5 A 1.5,1.5 0 0 1 13.5,5 L 13.5,12 A 1.5,1.5 0 0 1 12,13.5 L 4,13.5 A 1.5,1.5 0 0 1 2.5,12 Z M 2.5,6.6 L 13.5,6.6'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Name}'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='ServiceNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 14.01,6.94 L 14.01,9.06 L 12.33,9.24 L 11.94,10.18 L 13.00,11.50 L 11.50,13.00 L 10.18,11.94 L 9.24,12.33 L 9.06,14.01 L 6.94,14.01 L 6.76,12.33 L 5.82,11.94 L 4.50,13.00 L 3.00,11.50 L 4.06,10.18 L 3.67,9.24 L 1.99,9.06 L 1.99,6.94 L 3.67,6.76 L 4.06,5.82 L 3.00,4.50 L 4.50,3.00 L 5.82,4.06 L 6.76,3.67 L 6.94,1.99 L 9.06,1.99 L 9.24,3.67 L 10.18,4.06 L 11.50,3.00 L 13.00,4.50 L 11.94,5.82 L 12.33,6.76 Z M 6.10,8 A 1.90,1.90 0 1 1 9.90,8 A 1.90,1.90 0 1 1 6.10,8'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='TaskNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 8,2 A 6,6 0 1 1 8,14 A 6,6 0 1 1 8,2 M 8,4.6 L 8,8.2 L 10.6,9.7'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
    <!-- 四页共用的空态模板：原来四个 StackPanel 逐字重复四份 -->
    <ControlTemplate x:Key='EmptyHint' TargetType='ContentControl'>
      <StackPanel>
        <Path Width='26' Height='26' Stretch='Uniform' Stroke='{DynamicResource BorderColor}'
              StrokeThickness='1.4' HorizontalAlignment='Center'
              Data='M 3,2 L 15,2 L 15,17 L 3,17 Z M 6,6 L 12,6 M 6,9.5 L 12,9.5 M 6,13 L 10,13'/>
        <TextBlock Text='没有匹配的项目' FontSize='12.5' Foreground='{DynamicResource TextSecondary}'
                   HorizontalAlignment='Center' Margin='0,10,0,0'/>
      </StackPanel>
    </ControlTemplate>
    <Style x:Key='CtxMenu' TargetType='ContextMenu'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ContextMenu'>
            <Border CornerRadius='8' Background='{DynamicResource MenuBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34'
                    Padding='4' Margin='8'>
              <Border.Effect>
                <DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='0.18' Color='#000000'/>
              </Border.Effect>
              <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='CtxItem' TargetType='MenuItem'>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='MenuItem'>
            <Border x:Name='bd' CornerRadius='5' Padding='10,7' Background='Transparent'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter Grid.Column='0' ContentSource='Header' VerticalAlignment='Center'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsHighlighted' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='CtxSep' TargetType='Separator'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Separator'>
            <Border Height='1' Background='{DynamicResource GridLine}' Margin='6,4'/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 页签：语义与左侧导航一致（选中底色 + 底部指示条 + 强调色文字） -->
    <Style x:Key='SegTab' TargetType='RadioButton'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RadioButton'>
            <Grid>
              <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='14,7'>
                <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='ind' Height='2' CornerRadius='1' VerticalAlignment='Bottom'
                      Margin='12,0,12,-1' Background='Transparent'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                <Setter TargetName='ind' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
                <Setter Property='FontWeight' Value='SemiBold'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='CtxNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 2.2,3.8 L 2.2,11.6 L 4.4,9.4 L 5.8,12.6 L 7.2,12.0 L 5.9,9.0 L 8.4,8.9 Z M 10.6,3.8 L 14.0,3.8 M 10.6,6.4 L 14.0,6.4 M 10.6,9.0 L 14.0,9.0'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
  </Grid.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <Grid x:Name='TitleBar' Grid.Row='0' MinHeight='48' Background='{DynamicResource TitleBarBg}'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center' Margin='7,0,0,0'>
        <Button x:Name='BtnHamburger' Style='{StaticResource WinBtnSmall}' ToolTip='折叠 / 展开导航'>
          <Path Width='14' Height='12' Stretch='Fill'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1.4' Data='M 0,1 L 12,1 M 0,6 L 12,6 M 0,11 L 12,11'/>
        </Button>
        <TextBlock Text='PCTools' FontSize='12.5' FontWeight='SemiBold' Margin='6,0,0,0'
                   Foreground='{DynamicResource TextPrimary}' VerticalAlignment='Center'/>
        <!-- 版本号由 AppInfo.Version 在构造时写入，这里只放占位，避免两处字面量漂移 -->
        <TextBlock x:Name='TitleVersion' Text='v1.1.0' FontSize='10.5' Margin='6,1,0,0'
                   Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
      </StackPanel>
      <!-- 高度由 32 的搜索框决定，交给 VerticalAlignment=Center 在 48 高的标题栏里居中；
           不要写死 Height='48'，标题栏被系统字号撑高时反而会偏移 -->
      <Grid x:Name='SearchHost' Grid.Column='1' Width='340' VerticalAlignment='Center'>
        <TextBox x:Name='SearchBox' Style='{StaticResource SearchBox}' ToolTip='搜索当前列表'/>
        <!-- 32.34 = 1.34 边框 + 10 内边距 + 13 图标 + 8 间距：与输入文字的起点对齐，
             否则一输入文字占位符会向左跳约 4.7 DIP -->
        <TextBlock x:Name='SearchHint' Text='搜索名称、路径或关键字' FontSize='12.5' Margin='32.34,0,0,0'
                   Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'
                   IsHitTestVisible='False'/>
      </Grid>
      <StackPanel Grid.Column='2' Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
        <Button x:Name='BtnAdmin' Style='{StaticResource CmdBtn}' Margin='0,0,4,0'
                Content='以管理员身份重启' ToolTip='修改系统级项目需要管理员权限'/>
        <Button x:Name='BtnTheme' Style='{StaticResource WinBtn}' ToolTip='切换深色 / 浅色主题'>
          <Grid Width='12' Height='12'>
            <Grid x:Name='IconLight'>
              <Path Fill='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    Data='M 6,1 A 5,5 0 0 0 6,11 Z'/>
              <Path Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    StrokeThickness='1.2'
                    Data='M 6,1 A 5,5 0 1 1 6,11 A 5,5 0 1 1 6,1'/>
            </Grid>
            <Grid x:Name='IconDark' Visibility='Collapsed'>
              <Path Fill='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    Data='M 6,1 A 5,5 0 0 1 6,11 Z'/>
              <Path Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    StrokeThickness='1.2'
                    Data='M 6,1 A 5,5 0 1 1 6,11 A 5,5 0 1 1 6,1'/>
            </Grid>
          </Grid>
        </Button>
        <Button x:Name='BtnMin' Style='{StaticResource WinBtn}' ToolTip='最小化'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1' Data='M 0.5,5 L 9.5,5'/>
        </Button>
        <Button x:Name='BtnMax' Style='{StaticResource WinBtn}' ToolTip='最大化 / 还原'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1' Data='M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z'/>
        </Button>
        <Button x:Name='BtnClose' Style='{StaticResource WinBtnClose}' ToolTip='关闭'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1.1' Data='M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5'/>
        </Button>
      </StackPanel>
    </Grid>

    <!-- 主体 -->
    <Grid Grid.Row='1'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column='0' Background='{DynamicResource NavBg}'
              BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,0,1.34,0'>
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
          </Grid.RowDefinitions>
          <ListBox x:Name='Nav' Grid.Row='0' Width='212' BorderThickness='0' Padding='6,8'
                   Background='Transparent' ItemContainerStyle='{StaticResource NavItem}'
                   ScrollViewer.HorizontalScrollBarVisibility='Disabled'
                   SelectedIndex='0'>
            <ListBoxItem ToolTip='启动应用'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 2.5,5 A 1.5,1.5 0 0 1 4,3.5 L 12,3.5 A 1.5,1.5 0 0 1 13.5,5 L 13.5,12 A 1.5,1.5 0 0 1 12,13.5 L 4,13.5 A 1.5,1.5 0 0 1 2.5,12 Z M 2.5,6.6 L 13.5,6.6'/>
                <TextBlock Text='启动应用' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='服务'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 14.01,6.94 L 14.01,9.06 L 12.33,9.24 L 11.94,10.18 L 13.00,11.50 L 11.50,13.00 L 10.18,11.94 L 9.24,12.33 L 9.06,14.01 L 6.94,14.01 L 6.76,12.33 L 5.82,11.94 L 4.50,13.00 L 3.00,11.50 L 4.06,10.18 L 3.67,9.24 L 1.99,9.06 L 1.99,6.94 L 3.67,6.76 L 4.06,5.82 L 3.00,4.50 L 4.50,3.00 L 5.82,4.06 L 6.76,3.67 L 6.94,1.99 L 9.06,1.99 L 9.24,3.67 L 10.18,4.06 L 11.50,3.00 L 13.00,4.50 L 11.94,5.82 L 12.33,6.76 Z M 6.10,8 A 1.90,1.90 0 1 1 9.90,8 A 1.90,1.90 0 1 1 6.10,8'/>
                <TextBlock Text='服务管理' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='计划任务'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 2,8 A 6,6 0 1 1 14,8 A 6,6 0 1 1 2,8 M 8,4.2 L 8,8 L 11.0,9.75'/>
                <TextBlock Text='计划任务' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='右键菜单'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 2.2,3.8 L 2.2,11.6 L 4.4,9.4 L 5.8,12.6 L 7.2,12.0 L 5.9,9.0 L 8.4,8.9 Z M 10.6,3.8 L 14.0,3.8 M 10.6,6.4 L 14.0,6.4 M 10.6,9.0 L 14.0,9.0'/>
                <TextBlock Text='右键菜单' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
          </ListBox>
          <Border Grid.Row='1' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
                  Padding='6,8,6,10'>
            <StackPanel>
              <Button x:Name='BtnAiConfig' Style='{StaticResource NavButton}' ToolTip='配置 AI 接口与模型'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                        StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                        Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        Data='M 8,2 C 8.5,5.3 10.7,7.5 14,8 C 10.7,8.5 8.5,10.7 8,14 C 7.5,10.7 5.3,8.5 2,8 C 5.3,7.5 7.5,5.3 8,2 Z'/>
                  <StackPanel x:Name='AiNavText' Margin='12,0,0,0' VerticalAlignment='Center'>
                    <TextBlock Text='配置 AI' Tag='navtext'/>
                  </StackPanel>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTransfer' Style='{StaticResource NavButton}' ToolTip='导入导出备份基线、AI 建议与分析'>
                <StackPanel Orientation='Horizontal'>
                  <!-- 软盘备份图标；与导入导出页头的 Data 必须同步修改 -->
                  <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                        StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                        Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        Data='M 15.4,17.4 H 4.6 A 1.8,1.8 0 0 1 2.8,15.6 V 4.4 A 1.8,1.8 0 0 1 4.6,2.6 H 13.9 L 17.2,5.9 V 15.6 A 1.8,1.8 0 0 1 15.4,17.4 Z M 13.9,17.4 V 10.6 H 6.1 V 17.4 M 6.5,2.6 V 6.8 H 13'/>
                  <StackPanel x:Name='TransferNavText' Margin='12,0,0,0' VerticalAlignment='Center'>
                    <TextBlock Text='导入导出' Tag='navtext'/>
                  </StackPanel>
                </StackPanel>
              </Button>
            </StackPanel>
          </Border>
        </Grid>
      </Border>

      <Grid Grid.Column='1' Background='{DynamicResource ContentBg}'>
        <Grid.RowDefinitions>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='*'/>
        </Grid.RowDefinitions>

        <!-- 命令栏 -->
        <Grid x:Name='CommandBar' Grid.Row='0' Margin='24,18,24,14'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <StackPanel Grid.Column='0' VerticalAlignment='Center'>
            <TextBlock x:Name='PageTitle' Text='启动应用' FontSize='20' FontWeight='SemiBold'
                       Foreground='{DynamicResource TextPrimary}'/>
            <TextBlock x:Name='PageSubtitle' Text='随 Windows 登录自动运行的注册表项与启动文件夹快捷方式'
                       FontSize='11.5' Foreground='{DynamicResource TextSecondary}' Margin='0,4,0,0'/>
          </StackPanel>
          <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
            <StackPanel x:Name='CmdStartup' Orientation='Horizontal'>
              <Button x:Name='BtnStartupEnable' Style='{StaticResource CmdBtnIcon}' Content='启用' Tag='{StaticResource IconCheck}'/>
              <Button x:Name='BtnStartupDisable' Style='{StaticResource CmdBtnIcon}' Content='禁用' Tag='{StaticResource IconDisable}'/>
              <Button x:Name='BtnStartupRefresh' Style='{StaticResource CmdBtnIcon}' Content='刷新' Tag='{StaticResource IconRefresh}'/>
              <Button x:Name='BtnStartupAi' Style='{StaticResource CmdBtnIcon}' Content='更新AI建议' Tag='{StaticResource IconSparkle}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'/>
            </StackPanel>
            <StackPanel x:Name='CmdService' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnServiceEnable' Style='{StaticResource CmdBtnIcon}' Content='启用' Tag='{StaticResource IconCheck}'/>
              <Button x:Name='BtnServiceDisable' Style='{StaticResource CmdBtnIcon}' Content='禁用' Tag='{StaticResource IconDisable}'/>
              <Button x:Name='BtnServiceStart' Style='{StaticResource CmdBtnIcon}' Content='启动' Tag='{StaticResource IconPlay}'/>
              <Button x:Name='BtnServiceStop' Style='{StaticResource CmdBtnIcon}' Content='停止' Tag='{StaticResource IconStop}'/>
              <Button x:Name='BtnServiceRefresh' Style='{StaticResource CmdBtnIcon}' Content='刷新' Tag='{StaticResource IconRefresh}'/>
              <Button x:Name='BtnServiceAi' Style='{StaticResource CmdBtnIcon}' Content='更新AI建议' Tag='{StaticResource IconSparkle}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'/>
            </StackPanel>
            <StackPanel x:Name='CmdTask' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnTaskEnable' Style='{StaticResource CmdBtnIcon}' Content='启用' Tag='{StaticResource IconCheck}'/>
              <Button x:Name='BtnTaskDisable' Style='{StaticResource CmdBtnIcon}' Content='禁用' Tag='{StaticResource IconDisable}'/>
              <Button x:Name='BtnTaskRun' Style='{StaticResource CmdBtnIcon}' Content='运行' Tag='{StaticResource IconPlay}'/>
              <Button x:Name='BtnTaskStop' Style='{StaticResource CmdBtnIcon}' Content='停止' Tag='{StaticResource IconStop}'/>
              <Button x:Name='BtnTaskRefresh' Style='{StaticResource CmdBtnIcon}' Content='刷新' Tag='{StaticResource IconRefresh}'/>
              <Button x:Name='BtnTaskAi' Style='{StaticResource CmdBtnIcon}' Content='更新AI建议' Tag='{StaticResource IconSparkle}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'/>
            </StackPanel>
            <StackPanel x:Name='CmdCtx' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnCtxEnable' Style='{StaticResource CmdBtnIcon}' Content='启用' Tag='{StaticResource IconCheck}'/>
              <Button x:Name='BtnCtxDisable' Style='{StaticResource CmdBtnIcon}' Content='禁用' Tag='{StaticResource IconDisable}'/>
              <Button x:Name='BtnCtxRestart' Style='{StaticResource CmdBtnIcon}' Content='重启资源管理器' Tag='{StaticResource IconRefresh}' ToolTip='重启资源管理器使更改生效（任务栏会闪烁 1-2 秒）'/>
              <Button x:Name='BtnCtxRefresh' Style='{StaticResource CmdBtnIcon}' Content='刷新' Tag='{StaticResource IconRefresh}'/>
              <Button x:Name='BtnCtxAi' Style='{StaticResource CmdBtnIcon}' Content='更新AI建议' Tag='{StaticResource IconSparkle}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'/>
            </StackPanel>
          </StackPanel>
        </Grid>

        <!-- 列表 -->
        <Border x:Name='ListHost' Grid.Row='1' Background='Transparent' Margin='10,0,10,10'>
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height='Auto'/>
              <RowDefinition Height='*'/>
            </Grid.RowDefinitions>
            <!-- 统计条：四页共用；左侧筛选、右侧备份与逐项 AI 分析入口 -->
            <Grid Grid.Row='0' Margin='2,0,0,6'>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <CheckBox x:Name='ChkBackupOnly' Grid.Column='0' Style='{StaticResource ChkBox}'
                        VerticalAlignment='Center' Margin='0,0,12,0'
                        Content='只显示与备份状态不一致的项目'/>
              <TextBlock x:Name='ListSummary' Grid.Column='1' HorizontalAlignment='Right'
                         VerticalAlignment='Center' Margin='0,0,10,0' FontSize='11.5'
                         Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'/>
              <Button x:Name='BtnBackup' Grid.Column='2' Style='{StaticResource CmdBtn}'
                      Height='26' Padding='9,0' FontSize='12' Margin='0,0,10,0'
                      Content='备份状态' ToolTip='把当前页全部项目的状态保存为对比基线（覆盖旧基线，不受搜索筛选影响）'/>
              <Border Grid.Column='3' Width='1.34' Height='12' Background='{DynamicResource BorderColor}'
                      VerticalAlignment='Center' Margin='0,0,10,0'/>
              <Button x:Name='BtnAnalyzeAll' Grid.Column='4' Style='{StaticResource CmdBtn}'
                      Height='26' Padding='9,0' FontSize='12'
                      ToolTip='逐项请求 AI 完整分析（项目介绍 + 处理建议），每项一次请求，消耗 Token 较多'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='10' Height='10' Stretch='Uniform' Stroke='{DynamicResource Accent}'
                        StrokeThickness='1.5' Fill='Transparent' Margin='0,0,5,0' VerticalAlignment='Center'
                        Data='{StaticResource IconSparkle}'/>
                  <TextBlock Text='更新AI分析' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </Grid>
            <Grid x:Name='PageStartup' Grid.Row='1'>
              <DataGrid x:Name='GridStartup' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='名称' Width='178' CellTemplate='{StaticResource StartupNameCell}' SortMemberPath='Name'/>
                  <DataGridTemplateColumn Header='备份状态' Width='96' CellTemplate='{StaticResource BackupCell}' SortMemberPath='BackupRank'/>
                  <DataGridTemplateColumn Header='状态' Width='92' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTextColumn Header='位置类型' Binding='{Binding Location}' Width='90' SortMemberPath='Location' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='作用范围' Binding='{Binding Scope}' Width='90' SortMemberPath='Scope' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='启动命令 / 目标路径' Binding='{Binding Command}' Width='240' MinWidth='160' SortMemberPath='Command' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyStartup' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageService' Grid.Row='1' Visibility='Collapsed'>
              <DataGrid x:Name='GridService' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='显示名称' Width='164' CellTemplate='{StaticResource ServiceNameCell}' SortMemberPath='Name'/>
                  <DataGridTextColumn Header='服务名称' Binding='{Binding ServiceName}' Width='100' SortMemberPath='ServiceName' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTemplateColumn Header='运行状态' Width='88' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='备份启动类型' Width='104' CellTemplate='{StaticResource BackupCell}' SortMemberPath='BackupRank'/>
                  <DataGridTemplateColumn Header='启动类型' Width='80' CellTemplate='{StaticResource StartTypeCell}' SortMemberPath='StartTypeRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTextColumn Header='服务类型' Binding='{Binding ServiceType}' Width='82' SortMemberPath='ServiceType' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='可执行路径' Binding='{Binding Path}' Width='190' MinWidth='150' SortMemberPath='Path' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyService' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageTask' Grid.Row='1' Visibility='Collapsed'>
              <DataGrid x:Name='GridTask' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='任务名称' Width='176' CellTemplate='{StaticResource TaskNameCell}' SortMemberPath='Name'/>
                  <DataGridTemplateColumn Header='备份状态' Width='96' CellTemplate='{StaticResource BackupCell}' SortMemberPath='BackupRank'/>
                  <DataGridTemplateColumn Header='状态' Width='88' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTextColumn Header='触发器' Binding='{Binding Trigger}' Width='118' SortMemberPath='Trigger' ElementStyle='{StaticResource TextCell}'/>
                  <DataGridTextColumn Header='执行操作' Binding='{Binding Action}' Width='200' MinWidth='160' SortMemberPath='Action' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='上次运行' Binding='{Binding LastRun}' Width='88' SortMemberPath='LastRun' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyTask' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageCtx' Grid.Row='1' Visibility='Collapsed'>
              <Grid.RowDefinitions>
                <RowDefinition Height='Auto'/>
                <RowDefinition Height='*'/>
              </Grid.RowDefinitions>
              <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,10'>
                <RadioButton x:Name='TabCtxAll' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='全局' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxDesktop' GroupName='CtxTab' IsChecked='True'
                             Style='{StaticResource SegTab}' Content='桌面' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxDrive' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='驱动器' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxFile' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxFolder' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件夹' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxAfo' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件和文件夹'/>
              </StackPanel>
              <Grid Grid.Row='1'>
                <DataGrid x:Name='GridCtx' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                  <DataGrid.Columns>
                    <DataGridTemplateColumn Header='名称' Width='200' CellTemplate='{StaticResource CtxNameCell}' SortMemberPath='Name'/>
                    <DataGridTemplateColumn Header='备份状态' Width='96' CellTemplate='{StaticResource BackupCell}' SortMemberPath='BackupRank'/>
                    <DataGridTemplateColumn Header='状态' Width='92' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                    <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                    <DataGridTextColumn Header='类型' Binding='{Binding Kind}' Width='88' SortMemberPath='Kind' ElementStyle='{StaticResource TextCell}'/>
                    <DataGridTextColumn Header='作用域' Binding='{Binding Scope}' Width='96' MinWidth='88' SortMemberPath='Scope' ElementStyle='{StaticResource SubCell}'/>
                    <DataGridTextColumn Header='标识' Binding='{Binding Id}' Width='250' MinWidth='180' SortMemberPath='Id' ElementStyle='{StaticResource SubCell}'/>
                  </DataGrid.Columns>
                </DataGrid>
                <ContentControl x:Name='EmptyCtx' HorizontalAlignment='Center' VerticalAlignment='Center'
                                Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
              </Grid>
            </Grid>
          </Grid>
        </Border>

        <!-- 设置页：配置 AI / 导入导出。盖住命令栏与列表，只保留左侧导航。
             这里不能再包 ScrollViewer：它会以无限高测量内容，页面的 * 行全部退化成 Auto，
             结果是页内滚动失效、页头和底部按钮被整页一起滚走。滚动交给各页面自己的 * 行 -->
        <Grid x:Name='SettingsHost' Grid.Row='0' Grid.RowSpan='2' Visibility='Collapsed'
              Background='{DynamicResource ContentBg}' ClipToBounds='True'>
          <ContentControl x:Name='SettingsContent'
                          HorizontalContentAlignment='Stretch' VerticalContentAlignment='Stretch'/>
        </Grid>
      </Grid>
    </Grid>

    <!-- 状态栏 -->
    <Border Grid.Row='2' Height='34' Background='{DynamicResource StatusBg}'
            BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0' Padding='24,0'>
      <Grid>
        <TextBlock x:Name='StatusText' Text='正在加载…' FontSize='11'
                   Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' Margin='0,0,12,0'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
          <TextBlock x:Name='CountText' FontSize='11'
                     Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
          <Border Width='1.34' Height='11' Background='{DynamicResource BorderColor}' Margin='10,0'/>
          <TextBlock Text='右键查看更多操作' FontSize='11'
                     Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Grid>";

        // 五个弹窗（Prompt/AiSettings/AiResult/Confirm/Progress）共用的按钮样式。
        // 原先各窗口 XAML 自带一份近似副本，触发器与圆角已互相漂移，这里统一成一份
        // 超集（悬停/按下/禁用三态全带）；各窗口只针对真实差异（IsCancel/IsDefault/Margin）
        // 用 BasedOn 写几行覆盖，不再整段复制
        internal static readonly string BtnStyles = @"
    <Style x:Key='PBtn' TargetType='Button'>
      <Setter Property='Height' Value='36'/>
      <Setter Property='MinWidth' Value='96'/>
      <Setter Property='Padding' Value='18,0'/>
      <Setter Property='Margin' Value='10,0,0,0'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Background' Value='{DynamicResource BtnBg}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <!-- 描边厚度用 1.34 DIP（约 4/3），不能改回 1：
                 1 DIP 在 150% 下是 1.5 设备像素，取整后落在半像素被抗锯齿吃掉；
                 4/3 DIP 在 150% 下是 2.000 设备像素，四边都能画成实线。
                 按钮高度也必须是 36 DIP（150% 下 54px，偶数）：34 DIP 是 51px 奇数，
                 起点半像素相位会让底部边框被挤成 0~1px，这才是「下边框消失」的真凶。 -->
            <Border x:Name='bd' CornerRadius='6' Background='{TemplateBinding Background}'
                    BorderBrush='{DynamicResource BtnBorder}' BorderThickness='1.34'
                    SnapsToDevicePixels='True' RenderOptions.EdgeMode='Aliased'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.45'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='PBtnPrimary' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Background' Value='{DynamicResource Accent}'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='Foreground' Value='#FFFFFF'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='6' Background='{TemplateBinding Background}'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Opacity' Value='0.9'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Opacity' Value='0.78'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.45'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 第三按钮：普通外观；必须显式关掉 IsCancel，否则 Esc 会在取消按钮之间打架 -->
    <Style x:Key='PBtnFlat' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='IsCancel' Value='False'/>
    </Style>";

        internal static readonly string Prompt = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource DialogBg}'>
  <Border.Resources>" + BtnStyles + @"  </Border.Resources>
  <Grid Margin='26,24,26,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='20' Height='20' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.7' Fill='Transparent'
              Data='M 10,1 L 19,5 L 19,12 C 19,17 15,20 10,22 C 5,20 1,17 1,12 L 1,5 Z M 6,11 L 9,14 L 14,8'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='需要管理员权限' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock Text='部分系统项目当前为只读状态' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <TextBlock Grid.Row='1' Margin='0,18,0,0' TextWrapping='Wrap' LineHeight='21' FontSize='12.5'
               Foreground='{DynamicResource TextSecondary}'
               Text='修改系统级服务、计划任务以及所有用户的启动项需要管理员权限。以管理员身份重新启动后即可执行启用、禁用、启动、停止等操作；选择普通权限继续则仅可查看列表。'/>
    <StackPanel Grid.Row='2' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,22,0,0'>
      <Button x:Name='BtnContinue' Content='以普通权限继续' Style='{StaticResource PBtn}'/>
      <Button x:Name='BtnRestartAdmin' Content='以管理员身份重启' Style='{StaticResource PBtnPrimary}'/>
    </StackPanel>
  </Grid>
</Border>";

        internal static readonly string AiSettings = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='Transparent'>
  <Border.Resources>" + BtnStyles + @"    <Style x:Key='PTiny' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Height' Value='28'/>
      <Setter Property='MinWidth' Value='50'/>
      <Setter Property='Padding' Value='10,0'/>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='Margin' Value='0,0,6,0'/>
    </Style>
    <!-- 列表行内的小按钮：激活 / 已激活（绿） / 删除（红）。
         注意 DataTemplate 里不能写 Click=，事件由 ListBox 的 AddHandler 统一接 -->
    <Style x:Key='PRowActivate' TargetType='Button' BasedOn='{StaticResource PTiny}'>
      <Setter Property='Content' Value='激活'/>
      <Setter Property='Margin' Value='0'/>
      <Setter Property='Padding' Value='8,0'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='FontSize' Value='10.5'/>
      <Setter Property='Focusable' Value='False'/>
      <Style.Triggers>
        <DataTrigger Binding='{Binding Active}' Value='True'>
          <Setter Property='Content' Value='已激活'/>
          <Setter Property='Foreground' Value='{DynamicResource SuccessFg}'/>
          <Setter Property='Background' Value='{DynamicResource SuccessBg}'/>
          <Setter Property='FontWeight' Value='SemiBold'/>
          <Setter Property='IsHitTestVisible' Value='False'/>
        </DataTrigger>
      </Style.Triggers>
    </Style>
    <Style x:Key='PRowDelete' TargetType='Button' BasedOn='{StaticResource PTiny}'>
      <Setter Property='Margin' Value='6,0,0,0'/>
      <Setter Property='Padding' Value='8,0'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='FontSize' Value='10.5'/>
      <Setter Property='Focusable' Value='False'/>
      <Setter Property='Foreground' Value='{DynamicResource DangerFg}'/>
    </Style>
    <Style x:Key='AField' TargetType='TextBox'>
      <Setter Property='Height' Value='36'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <!-- 与 PBtn 同一套写法：1 DIP 描边在 125%/150% 缩放下会落在半像素，
                 底部最先被抗锯齿吃掉；1.34 DIP + 像素对齐后四边都是实线 -->
            <Border x:Name='bd' CornerRadius='7' Background='{DynamicResource BtnBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34'
                    SnapsToDevicePixels='True' RenderOptions.EdgeMode='Aliased' Padding='11,0'>
              <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsKeyboardFocusWithin' Value='True'>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='ALabel' TargetType='TextBlock'>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Margin' Value='0,0,0,6'/>
    </Style>
    <Style x:Key='PList' TargetType='ListBox'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
      <Setter Property='ItemContainerStyle'>
        <Setter.Value>
          <Style TargetType='ListBoxItem'>
            <Setter Property='Margin' Value='0,0,0,3'/>
            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
              <Setter.Value>
                <ControlTemplate TargetType='ListBoxItem'>
                  <Border x:Name='bd' CornerRadius='7' Background='Transparent' Padding='10,7'>
                    <ContentPresenter/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
                    </Trigger>
                    <Trigger Property='IsSelected' Value='True'>
                      <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </Setter.Value>
      </Setter>
    </Style>
  </Border.Resources>
  <Grid Margin='28,24,28,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <!-- 页头：与列表页标题保持同一字号层级 -->
    <Grid Grid.Row='0' Margin='0,0,0,18'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
      </Grid.ColumnDefinitions>
      <StackPanel Orientation='Horizontal'>
        <Border Width='42' Height='42' CornerRadius='12' Background='{DynamicResource SelectionBg}'>
          <Path Width='21' Height='21' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.6'
                Fill='Transparent'
                Data='M 10,2.4 L 11.9,8.1 L 17.6,10 L 11.9,11.9 L 10,17.6 L 8.1,11.9 L 2.4,10 L 8.1,8.1 Z'/>
        </Border>
        <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
          <TextBlock Text='配置 AI' FontSize='20' FontWeight='SemiBold'
                     Foreground='{DynamicResource TextPrimary}'/>
          <TextBlock Text='接口、模型与密钥只保存在本机；同一时间启用一套配置'
                     FontSize='11.5' Foreground='{DynamicResource TextSecondary}' Margin='0,4,0,0'/>
        </StackPanel>
      </StackPanel>
      <Border Grid.Column='1' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34' CornerRadius='14' Padding='11,5' VerticalAlignment='Center'>
        <TextBlock x:Name='AiCapBadge' Text='最多 10 套' FontSize='11' Foreground='{DynamicResource TextSecondary}'/>
      </Border>
    </Grid>

    <!-- 主体：左侧配置列表 + 右侧连接信息，两块等高卡片 -->
    <Grid Grid.Row='1'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*' MaxWidth='332'/>
        <ColumnDefinition Width='16'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>

      <Border Grid.Column='0' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34' CornerRadius='10' Padding='12'>
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
          </Grid.RowDefinitions>
          <Grid Grid.Row='0' Margin='2,0,2,8'>
            <TextBlock Text='接口配置' FontSize='12.5' FontWeight='SemiBold'
                       Foreground='{DynamicResource TextPrimary}'/>
            <TextBlock Text='选中后右侧编辑' HorizontalAlignment='Right' FontSize='10.5'
                       Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
          </Grid>
          <ListBox x:Name='FldList' Grid.Row='1' Style='{StaticResource PList}'>
            <ListBox.ItemTemplate>
              <DataTemplate>
                <Grid>
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width='*'/>
                    <ColumnDefinition Width='Auto'/>
                  </Grid.ColumnDefinitions>
                  <StackPanel Grid.Column='0' Margin='0,0,8,0'>
                    <TextBlock Text='{Binding Title}' FontSize='12.5' Foreground='{DynamicResource TextPrimary}'
                               TextTrimming='CharacterEllipsis'/>
                    <TextBlock Text='{Binding Sub}' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                               TextTrimming='CharacterEllipsis' ToolTip='{Binding Sub}' Margin='0,2,0,0'/>
                    <TextBlock Text='{Binding Stats}' ToolTip='{Binding StatsTip}' FontSize='10'
                               Foreground='{DynamicResource TextSecondary}'
                               TextTrimming='CharacterEllipsis' Margin='0,3,0,0'/>
                  </StackPanel>
                  <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
                    <Button Style='{StaticResource PRowActivate}' Tag='activate'/>
                    <Button Style='{StaticResource PRowDelete}' Tag='delete' Content='删除'
                            IsEnabled='{Binding CanDelete}'/>
                  </StackPanel>
                </Grid>
              </DataTemplate>
            </ListBox.ItemTemplate>
          </ListBox>
          <Button x:Name='BtnNew' Grid.Row='2' Content='新增配置' Style='{StaticResource PBtn}'
                  Margin='0,10,0,2' ToolTip='新增一套配置（最多 10 套）'/>
        </Grid>
      </Border>

      <Border Grid.Column='2' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34' CornerRadius='10'>
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
          </Grid.RowDefinitions>
          <ScrollViewer Grid.Row='0' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
            <StackPanel Margin='22,20,22,18'>
              <TextBlock Text='连接信息' FontSize='13.5' FontWeight='SemiBold'
                         Foreground='{DynamicResource TextPrimary}'/>
              <TextBlock Text='兼容 OpenAI Chat Completions 协议，接口地址以 /v1/chat/completions 结尾。'
                         FontSize='11.5' Foreground='{DynamicResource TextSecondary}'
                         TextWrapping='Wrap' Margin='0,4,0,18'/>

              <TextBlock Text='配置名称' Style='{StaticResource ALabel}'/>
              <TextBox x:Name='FldName' Style='{StaticResource AField}' Margin='0,0,0,14'
                       ToolTip='用于在列表里区分，可留空（自动命名为「配置 N」）'/>

              <TextBlock Text='接口地址' Style='{StaticResource ALabel}'/>
              <TextBox x:Name='FldEndpoint' Style='{StaticResource AField}' Margin='0,0,0,14'
                       ToolTip='例如 https://api.deepseek.com/v1/chat/completions'/>

              <TextBlock Text='模型名称' Style='{StaticResource ALabel}'/>
              <TextBox x:Name='FldModel' Style='{StaticResource AField}' Margin='0,0,0,14'
                       ToolTip='例如 deepseek-chat / gpt-4o-mini / qwen-plus'/>

              <TextBlock Text='API Key' Style='{StaticResource ALabel}'/>
              <TextBox x:Name='FldKey' Style='{StaticResource AField}'
                       ToolTip='DPAPI 按当前用户加密，保存在同目录的 PCToolsAI.sqlite'/>
              <TextBlock Text='密钥使用 Windows DPAPI 按当前用户加密，换账户或换机器都无法解密。'
                         FontSize='10.5' Foreground='{DynamicResource TextSecondary}' Margin='0,7,0,0'/>
            </StackPanel>
          </ScrollViewer>
          <Border Grid.Row='1' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
                  Padding='22,12'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column='0' Text='保存后生效' FontSize='11'
                         Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'
                         VerticalAlignment='Center'/>
              <StackPanel Grid.Column='1' Orientation='Horizontal'>
                <Button x:Name='BtnClearOne' Content='清空此配置' Style='{StaticResource PBtn}'
                        Margin='0' Padding='12,0'
                        ToolTip='清空右侧四个输入框；点「保存此配置」后才写入数据库'/>
                <Button x:Name='BtnSaveOne' Content='保存此配置' Style='{StaticResource PBtn}'
                        Margin='8,0,0,0'
                        ToolTip='只保存当前这套配置，不关闭页面（底部「保存全部」写入全部修改）'/>
              </StackPanel>
            </Grid>
          </Border>
        </Grid>
      </Border>
    </Grid>

    <!-- 底部操作栏：状态说明在左，测试与保存固定在右 -->
    <Border Grid.Row='2' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
            Margin='0,18,0,0' Padding='0,14,0,0'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='*'/>
          <ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <StackPanel x:Name='AiStatusHost' Visibility='Collapsed' VerticalAlignment='Center' Margin='0,0,16,0'>
          <TextBlock x:Name='AiStatus' FontSize='11.5' TextWrapping='Wrap' LineHeight='18'
                     Foreground='{DynamicResource TextSecondary}'/>
        </StackPanel>
        <StackPanel Grid.Column='1' Orientation='Horizontal'>
          <Button x:Name='BtnCancel' Content='取消' Style='{StaticResource PBtn}'/>
          <Button x:Name='BtnTest' Content='测试连接' Style='{StaticResource PBtn}'/>
          <Button x:Name='BtnSave' Content='保存' Style='{StaticResource PBtnPrimary}'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Border>";
        internal static readonly string TransferPage = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='Transparent'>
  <Border.Resources>" + BtnStyles + @"    <Style x:Key='TMini' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Height' Value='24'/>
      <Setter Property='MinWidth' Value='0'/>
      <Setter Property='Padding' Value='8,0'/>
      <Setter Property='FontSize' Value='11'/>
      <Setter Property='Margin' Value='6,0,0,0'/>
    </Style>
    <Style x:Key='TMode' TargetType='RadioButton'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RadioButton'>
            <Border x:Name='bd' CornerRadius='7' Background='Transparent'
                    BorderBrush='Transparent' BorderThickness='1.2'
                    SnapsToDevicePixels='True' Padding='18,5'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <!-- 选中态与主按钮（PBtnPrimary）同色：纯蓝底 + 白字 -->
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='#FFFFFF'/>
                <Setter Property='FontWeight' Value='SemiBold'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='TSelect' TargetType='CheckBox'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Margin' Value='0,0,8,8'/>
      <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='CheckBox'>
            <Border x:Name='bd' CornerRadius='8' Background='{DynamicResource BtnBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='10,8'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <StackPanel Grid.Column='0' Margin='0,0,8,0'>
                  <ContentPresenter/>
                  <TextBlock Text='{TemplateBinding Tag}' FontSize='10' Margin='0,2,0,0'
                             TextTrimming='CharacterEllipsis'
                             Foreground='{DynamicResource TextSecondary}'/>
                </StackPanel>
                <Border x:Name='box' Grid.Column='1' Width='16' Height='16' CornerRadius='4'
                        Background='Transparent' BorderBrush='{DynamicResource BorderColor}'
                        BorderThickness='1.34' VerticalAlignment='Center'>
                  <Path x:Name='tick' Width='9' Height='9' Stretch='Uniform' Stroke='#FFFFFF'
                        StrokeThickness='1.9' Visibility='Collapsed' Data='M 1,4.4 L 3.4,6.8 L 8,2'/>
                </Border>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource SelectionBg}'/>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
                <Setter Property='FontWeight' Value='SemiBold'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.45'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='TCheck' TargetType='CheckBox'>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='CheckBox'>
            <Border x:Name='box' Width='16' Height='16' CornerRadius='4'
                    Background='Transparent' BorderBrush='{DynamicResource BorderColor}'
                    BorderThickness='1.34'>
              <Path x:Name='tick' Width='9' Height='9' Stretch='Uniform' Stroke='#FFFFFF'
                    StrokeThickness='1.9' Visibility='Collapsed' Data='M 1,4.4 L 3.4,6.8 L 8,2'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
                <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='box' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='TChip'>
      <Border Margin='0,0,8,8' CornerRadius='7' Padding='9,4'
              Background='{DynamicResource BtnBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34'>
        <StackPanel Orientation='Horizontal'>
          <TextBlock Text='{Binding Title}' FontSize='11' Foreground='{DynamicResource TextPrimary}'
                     TextTrimming='CharacterEllipsis' MaxWidth='112'/>
          <Border CornerRadius='6' Padding='5,0' Margin='6,0,0,0' Background='{Binding BadgeBg}'>
            <TextBlock Text='{Binding Badge}' FontSize='10' Foreground='{Binding BadgeFg}'/>
          </Border>
        </StackPanel>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding IsEmpty}' Value='True'>
          <Setter Property='Opacity' Value='0.55'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>
    <DataTemplate x:Key='TSetRow'>
      <Border x:Name='row' CornerRadius='8' Padding='11,8' Margin='0,0,2,6'
              Background='{DynamicResource BtnBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34'>
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <CheckBox Grid.Column='0' Style='{StaticResource TCheck}' VerticalAlignment='Center'
                    IsChecked='{Binding Selected, Mode=TwoWay}'
                    IsEnabled='{Binding CanImport}' ToolTip='{Binding DisabledTip}'/>
          <StackPanel Grid.Column='1' Margin='10,0,10,0' VerticalAlignment='Center'>
            <TextBlock Text='{Binding Title}' FontSize='12.5' Foreground='{DynamicResource TextPrimary}'
                       TextTrimming='CharacterEllipsis'/>
            <TextBlock Text='{Binding Note}' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                       TextTrimming='CharacterEllipsis' ToolTip='{Binding Note}' Margin='0,2,0,0'/>
          </StackPanel>
          <Border Grid.Column='2' CornerRadius='7' Padding='6,1' Background='{DynamicResource HoverBg}'
                  VerticalAlignment='Center'>
            <TextBlock Text='{Binding CountText}' FontSize='10.5'
                       Foreground='{DynamicResource TextSecondary}'/>
          </Border>
          <Border Grid.Column='3' CornerRadius='7' Padding='6,1' Margin='6,0,0,0'
                  Background='{Binding KindBg}' VerticalAlignment='Center'>
            <TextBlock Text='{Binding KindText}' FontSize='10' Foreground='{Binding KindFg}'/>
          </Border>
        </Grid>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding Selected}' Value='True'>
          <Setter TargetName='row' Property='BorderBrush' Value='{DynamicResource Accent}'/>
          <Setter TargetName='row' Property='Background' Value='{DynamicResource SelectionBg}'/>
        </DataTrigger>
        <DataTrigger Binding='{Binding CanImport}' Value='False'>
          <Setter TargetName='row' Property='Opacity' Value='0.55'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>
  </Border.Resources>
  <Grid Margin='28,22,28,22'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <!-- 页头 -->
    <Grid Grid.Row='0' Margin='0,0,0,16'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
      </Grid.ColumnDefinitions>
      <StackPanel Orientation='Horizontal'>
        <Border Width='42' Height='42' CornerRadius='12' Background='{DynamicResource SelectionBg}'>
          <!-- 软盘备份图标；与左侧导航 BtnTransfer 的 Data 必须同步修改 -->
          <Path Width='21' Height='21' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.6'
                Fill='Transparent' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                Data='M 15.4,17.4 H 4.6 A 1.8,1.8 0 0 1 2.8,15.6 V 4.4 A 1.8,1.8 0 0 1 4.6,2.6 H 13.9 L 17.2,5.9 V 15.6 A 1.8,1.8 0 0 1 15.4,17.4 Z M 13.9,17.4 V 10.6 H 6.1 V 17.4 M 6.5,2.6 V 6.8 H 13'/>
        </Border>
        <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
          <TextBlock Text='导入导出' FontSize='20' FontWeight='SemiBold'
                     Foreground='{DynamicResource TextPrimary}'/>
          <TextBlock Text='多选项目与类型一次打包；导入时先选文件，再勾选要恢复的数据集'
                     FontSize='11.5' Foreground='{DynamicResource TextSecondary}' Margin='0,4,0,0'/>
        </StackPanel>
      </StackPanel>
      <Border Grid.Column='1' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34' CornerRadius='14' Padding='11,5' VerticalAlignment='Center'>
        <TextBlock Text='.pct · XML' FontSize='11' Foreground='{DynamicResource TextSecondary}'/>
      </Border>
    </Grid>

    <!-- 模式切换 -->
    <Grid Grid.Row='1' Margin='0,0,0,16'>
      <Border HorizontalAlignment='Left' Background='{DynamicResource BtnBg}'
              BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34'
              CornerRadius='9' Padding='2'>
        <StackPanel Orientation='Horizontal'>
          <RadioButton x:Name='OpExport' GroupName='OpMode' Style='{StaticResource TMode}'
                       Content='导出' IsChecked='True'/>
          <RadioButton x:Name='OpImport' GroupName='OpMode' Style='{StaticResource TMode}'
                       Content='导入'/>
        </StackPanel>
      </Border>
      <TextBlock x:Name='ModeHint' HorizontalAlignment='Right' VerticalAlignment='Center'
                 FontSize='11' Foreground='{DynamicResource TextSecondary}'
                 Text='数据集 = 所选项目 × 所选类型'/>
    </Grid>

    <!-- 导出与导入各自管理滚动，底部主按钮始终可见。
         这里不能再整个包一层 ScrollViewer：它会以无限高测量内容，
         页面的 * 行与列表的 * 行全部退化成 Auto，列表就永远滚不动 -->
    <Grid Grid.Row='2'>
      <ScrollViewer x:Name='ExportScroll' VerticalScrollBarVisibility='Auto'
                    HorizontalScrollBarVisibility='Disabled' Padding='0,0,2,0'>
        <StackPanel x:Name='ExportPanel'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='1*'/>
          <ColumnDefinition Width='12'/>
          <ColumnDefinition Width='1*'/>
        </Grid.ColumnDefinitions>

        <Border Grid.Column='0' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
                BorderThickness='1.34' CornerRadius='10' Padding='14'>
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height='Auto'/>
              <RowDefinition Height='*'/>
              <RowDefinition Height='Auto'/>
            </Grid.RowDefinitions>
            <Grid Grid.Row='0' Margin='0,0,0,10'>
              <StackPanel Orientation='Horizontal'>
                <Border Width='22' Height='22' CornerRadius='11' Background='{DynamicResource SelectionBg}'>
                  <TextBlock Text='1' FontSize='11' FontWeight='SemiBold' Foreground='{DynamicResource Accent}'
                             HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <TextBlock Text='项目' FontSize='13' FontWeight='SemiBold' Margin='8,0,0,0'
                           VerticalAlignment='Center' Foreground='{DynamicResource TextPrimary}'/>
              </StackPanel>
              <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                <TextBlock x:Name='PrjCount' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                         VerticalAlignment='Center' Margin='0,0,4,0'/>
                <Button x:Name='PrjAll' Content='全选' Style='{StaticResource TMini}'/>
                <Button x:Name='PrjNone' Content='清空' Style='{StaticResource TMini}'/>
              </StackPanel>
            </Grid>
            <UniformGrid Grid.Row='1' Columns='2'>
              <CheckBox x:Name='PrjStartup' Style='{StaticResource TSelect}' Content='启动项'
                        Tag='Run 与启动文件夹' IsChecked='True'/>
              <CheckBox x:Name='PrjService' Style='{StaticResource TSelect}' Content='服务'
                        Tag='服务与启动类型'/>
              <CheckBox x:Name='PrjTask' Style='{StaticResource TSelect}' Content='计划任务'
                        Tag='启用与禁用状态'/>
              <CheckBox x:Name='PrjCtx' Style='{StaticResource TSelect}' Content='右键菜单'
                        Tag='Shell 扩展与动词'/>
            </UniformGrid>
            <TextBlock Grid.Row='2' Text='未勾选的项目不参与打包' FontSize='10.5'
                       Foreground='{DynamicResource TextSecondary}' Margin='2,2,0,0'/>
          </Grid>
        </Border>

        <Border Grid.Column='2' Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
                BorderThickness='1.34' CornerRadius='10' Padding='14'>
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height='Auto'/>
              <RowDefinition Height='*'/>
              <RowDefinition Height='Auto'/>
            </Grid.RowDefinitions>
            <Grid Grid.Row='0' Margin='0,0,0,10'>
              <StackPanel Orientation='Horizontal'>
                <Border Width='22' Height='22' CornerRadius='11' Background='{DynamicResource SelectionBg}'>
                  <TextBlock Text='2' FontSize='11' FontWeight='SemiBold' Foreground='{DynamicResource Accent}'
                             HorizontalAlignment='Center' VerticalAlignment='Center'/>
                </Border>
                <TextBlock Text='类型' FontSize='13' FontWeight='SemiBold' Margin='8,0,0,0'
                           VerticalAlignment='Center' Foreground='{DynamicResource TextPrimary}'/>
              </StackPanel>
              <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
                <TextBlock x:Name='TypCount' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                         VerticalAlignment='Center' Margin='0,0,4,0'/>
                <Button x:Name='TypAll' Content='全选' Style='{StaticResource TMini}'/>
                <Button x:Name='TypNone' Content='清空' Style='{StaticResource TMini}'/>
              </StackPanel>
            </Grid>
            <UniformGrid Grid.Row='1' Columns='2'>
              <CheckBox x:Name='TyBackup' Style='{StaticResource TSelect}' Content='备份状态'
                        Tag='备份基线；服务为启动类型' IsChecked='True'/>
              <CheckBox x:Name='TyAdvice' Style='{StaticResource TSelect}' Content='AI建议'
                        Tag='批量分析的结论缓存'/>
              <CheckBox x:Name='TyAnalysis' Style='{StaticResource TSelect}' Content='AI分析'
                        Tag='逐项分析的详细说明'/>
            </UniformGrid>
            <TextBlock Grid.Row='2' Text='每种类型会应用到所有已选项目' FontSize='10.5'
                       Foreground='{DynamicResource TextSecondary}' Margin='2,2,0,0'/>
          </Grid>
        </Border>
      </Grid>

      <Border Background='{DynamicResource CardBg}' BorderBrush='{DynamicResource BorderColor}'
              BorderThickness='1.34' CornerRadius='10' Padding='14,12' Margin='0,14,0,0'>
        <StackPanel>
          <Grid Margin='0,0,0,8'>
            <StackPanel Orientation='Horizontal'>
              <Border Width='22' Height='22' CornerRadius='11' Background='{DynamicResource SelectionBg}'>
                <TextBlock Text='3' FontSize='11' FontWeight='SemiBold' Foreground='{DynamicResource Accent}'
                           HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Border>
              <TextBlock Text='数据集' FontSize='13' FontWeight='SemiBold' Margin='8,0,0,0'
                         VerticalAlignment='Center' Foreground='{DynamicResource TextPrimary}'/>
            </StackPanel>
            <TextBlock x:Name='SumCount' HorizontalAlignment='Right' VerticalAlignment='Center'
                       FontSize='11' Foreground='{DynamicResource TextSecondary}'/>
          </Grid>
          <ItemsControl x:Name='Chips' ItemTemplate='{StaticResource TChip}'>
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
          </ItemsControl>
          <TextBlock x:Name='SumEmpty' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                     Visibility='Collapsed' Margin='0,2,0,0'/>
        </StackPanel>
      </Border>
        </StackPanel>
      </ScrollViewer>

      <!-- 导入面板：文件卡与列表标题固定，只有数据集列表滚动。
           FileEmpty / FileCard 叠在同一格，显隐互斥由 C# 保证 -->
      <Grid x:Name='ImportPanel' Visibility='Collapsed'>
        <Grid.RowDefinitions>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='*'/>
        </Grid.RowDefinitions>
        <Grid Grid.Row='0'>
      <Border x:Name='FileEmpty' Background='{DynamicResource CardBg}'
              BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' CornerRadius='10'
              Padding='24,20'>
        <StackPanel HorizontalAlignment='Center'>
          <Border Width='52' Height='52' CornerRadius='14' Background='{DynamicResource SelectionBg}'
                  HorizontalAlignment='Center'>
            <Path Width='24' Height='24' Stretch='Uniform' Stroke='{DynamicResource Accent}'
                  StrokeThickness='1.6' Fill='Transparent'
                  Data='M 4,1.5 L 10,1.5 L 14,5.5 L 14,14.5 L 4,14.5 Z M 10,1.5 L 10,5.5 L 14,5.5 M 9,8 L 9,12 M 7,10 L 9,12 L 11,10'/>
          </Border>
          <TextBlock Text='尚未选择 .pct 文件' FontSize='14' FontWeight='SemiBold'
                     Foreground='{DynamicResource TextPrimary}' HorizontalAlignment='Center' Margin='0,12,0,0'/>
          <TextBlock Text='支持 v1 单数据集与 v2 多数据集；文件上限 16 MB，解析时禁用 DTD'
                     FontSize='11' Foreground='{DynamicResource TextSecondary}'
                     HorizontalAlignment='Center' Margin='0,5,0,14'/>
          <Button x:Name='PickFile' Content='选择 .pct 文件…' Style='{StaticResource PBtnPrimary}'
                  MinWidth='150' HorizontalAlignment='Center'/>
        </StackPanel>
      </Border>

      <Border x:Name='FileCard' Visibility='Collapsed' Background='{DynamicResource CardBg}'
              BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' CornerRadius='10'
              Padding='14,11'>
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='Auto'/>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <Border Width='32' Height='32' CornerRadius='9' Background='{DynamicResource SelectionBg}'
                  VerticalAlignment='Center'>
            <Path Width='16' Height='16' Stretch='Uniform' Stroke='{DynamicResource Accent}'
                  StrokeThickness='1.6' Fill='Transparent'
                  Data='M 3,1 L 8,1 L 12,5 L 12,13 L 3,13 Z M 8,1 L 8,5 L 12,5'/>
          </Border>
          <StackPanel Grid.Column='1' Margin='11,0,12,0' VerticalAlignment='Center'>
            <TextBlock x:Name='FileName' FontSize='13' Foreground='{DynamicResource TextPrimary}'
                       TextTrimming='CharacterEllipsis'/>
            <TextBlock x:Name='FileMeta' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                       TextTrimming='CharacterEllipsis' Margin='0,2,0,0'/>
          </StackPanel>
          <Button x:Name='Repick' Grid.Column='2' Content='重新选择…' Style='{StaticResource TMini}'
                  VerticalAlignment='Center'/>
        </Grid>
      </Border>
        </Grid>

      <Grid x:Name='SetPickHead' Grid.Row='1' Visibility='Collapsed' Margin='2,16,2,8'>
        <TextBlock Text='选择要导入的数据集' FontSize='13' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}' VerticalAlignment='Center'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
          <TextBlock x:Name='ImpCount' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                         VerticalAlignment='Center' Margin='0,0,4,0'/>
          <Button x:Name='ImpAll' Content='全选' Style='{StaticResource TMini}'/>
          <Button x:Name='ImpNone' Content='清空' Style='{StaticResource TMini}'/>
        </StackPanel>
      </Grid>

      <ScrollViewer x:Name='SetScroll' Grid.Row='2' VerticalScrollBarVisibility='Auto'
                    HorizontalScrollBarVisibility='Disabled' Visibility='Collapsed'>
        <ItemsControl x:Name='SetList' ItemTemplate='{StaticResource TSetRow}'/>
      </ScrollViewer>
      </Grid>
    </Grid>

    <!-- 底部操作条 -->
    <Border Grid.Row='3' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
            Margin='0,18,0,0' Padding='0,14,0,0'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='*'/>
          <ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <TextBlock x:Name='TransHint' Grid.Column='0' TextWrapping='Wrap' LineHeight='18'
                   Margin='2,0,16,0' FontSize='11.5' Foreground='{DynamicResource TextSecondary}'
                   VerticalAlignment='Center'/>
        <Button x:Name='BtnTransRun' Grid.Column='1' Style='{StaticResource PBtnPrimary}'
                MinWidth='168' VerticalAlignment='Center' Content='导出为 .pct…'/>
      </Grid>
    </Border>

    <TextBlock x:Name='TransStatus' Grid.Row='4' Margin='2,9,0,0' TextWrapping='Wrap' LineHeight='19'
               FontSize='12' Foreground='{DynamicResource TextSecondary}' Visibility='Collapsed'/>
  </Grid>
</Border>";
        internal static readonly string AiResult = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource DialogBg}'>
  <Border.Resources>" + BtnStyles + @"    <Style x:Key='AOutput' TargetType='TextBox'>
      <Setter Property='IsReadOnly' Value='True'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Padding' Value='0'/>
      <Setter Property='TextWrapping' Value='Wrap'/>
      <Setter Property='AcceptsReturn' Value='True'/>
      <Setter Property='VerticalScrollBarVisibility' Value='Auto'/>
      <Setter Property='VerticalContentAlignment' Value='Top'/>
    </Style>
    <Style x:Key='SectionLabel' TargetType='TextBlock'>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Margin' Value='2,0,0,8'/>
    </Style>
  </Border.Resources>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <!-- 内容区用 * 行：窗口被 MaxHeight 夹住时这里才会让出空间给滚动条，
           固定 Auto 行会把 530 DIP 的内容整块顶出可视区，底部按钮跟着出屏 -->
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='20' Height='20' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.6' Fill='Transparent'
              Data='M 10,2.4 L 11.9,8.1 L 17.6,10 L 11.9,11.9 L 10,17.6 L 8.1,11.9 L 2.4,10 L 8.1,8.1 Z'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='AI 分析' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='AiSubject' FontSize='11.5' TextTrimming='CharacterEllipsis'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <Grid Grid.Row='1'>
      <ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'
                    Padding='0,0,2,0'>
      <StackPanel>
        <TextBlock Text='项目介绍' Style='{StaticResource SectionLabel}'/>
        <Border Height='132' CornerRadius='8' Background='{DynamicResource CardBg}'
                BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='14,12'
                Margin='0,0,0,14'>
          <TextBox x:Name='AiBody' Style='{StaticResource AOutput}'/>
        </Border>
        <TextBlock x:Name='AdviceHeading' Text='处理建议' Style='{StaticResource SectionLabel}'/>
        <!-- 单份时只看右列并横跨整行；库里有两份时左右并排对比 -->
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='16'/>
            <ColumnDefinition Width='*'/>
          </Grid.ColumnDefinitions>
          <StackPanel x:Name='OldPane' Grid.Column='0' Visibility='Collapsed'>
            <TextBlock x:Name='AiCapOld' FontSize='11' FontWeight='SemiBold' Margin='2,0,0,8'
                       Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'/>
            <Border Height='172' CornerRadius='8' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='14,12'>
              <DockPanel>
                <TextBlock x:Name='AdviceTagOld' DockPanel.Dock='Top' FontSize='15' FontWeight='SemiBold'
                           Margin='0,0,0,6' Visibility='Collapsed'/>
                <TextBox x:Name='AiAdviceOld' Style='{StaticResource AOutput}'/>
              </DockPanel>
            </Border>
          </StackPanel>
          <StackPanel x:Name='NewPane' Grid.Column='0' Grid.ColumnSpan='3'>
            <TextBlock x:Name='AiCapNew' FontSize='11' FontWeight='SemiBold' Margin='2,0,0,8'
                       Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'
                       Visibility='Collapsed'/>
            <Border Height='172' CornerRadius='8' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='14,12'>
              <DockPanel>
                <TextBlock x:Name='AdviceTag' DockPanel.Dock='Top' FontSize='15' FontWeight='SemiBold'
                           Margin='0,0,0,6' Visibility='Collapsed'/>
                <TextBox x:Name='AiAdvice' Style='{StaticResource AOutput}'/>
              </DockPanel>
            </Border>
          </StackPanel>
        </Grid>
      </StackPanel>
      </ScrollViewer>
      <!-- 浮层：请求期间盖住两个信息框。默认收起，构造代码按需打开，
           避免 FindName 之前出异常时窗口挂着假加载态 -->
      <Border x:Name='AiLoading' Background='{DynamicResource DialogBg}' CornerRadius='8' Visibility='Collapsed'>
        <StackPanel HorizontalAlignment='Center' VerticalAlignment='Center'>
          <Grid x:Name='AiSpinHost' Width='28' Height='28' HorizontalAlignment='Center'>
            <Ellipse Width='24' Height='24' Stroke='{DynamicResource BorderColor}' StrokeThickness='2.4'/>
            <Path x:Name='AiSpinner' Width='24' Height='24' Stretch='None'
                  Stroke='{DynamicResource Accent}' StrokeThickness='2.4'
                  StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                  Data='M 13,1.4 A 11.6,11.6 0 1 1 1.4,13'/>
          </Grid>
          <TextBlock x:Name='AiLoadingText' Text='正在获取 AI 数据…' FontSize='12.5'
                     HorizontalAlignment='Center' Margin='0,14,0,0' MaxWidth='420'
                     TextWrapping='Wrap' TextAlignment='Center' LineHeight='20'
                     Foreground='{DynamicResource TextSecondary}'/>
        </StackPanel>
      </Border>
    </Grid>
    <Border Grid.Row='2' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
            Padding='0,14,0,0' Margin='0,16,0,0'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='*'/>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <TextBlock x:Name='AiMeta' Grid.Column='0' FontSize='11' VerticalAlignment='Center'
                   Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'
                   Margin='0,0,12,0'/>
        <TextBlock x:Name='AiUsage' Grid.Column='1' FontSize='11' VerticalAlignment='Center'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,0,14,0'/>
        <StackPanel Grid.Column='2' Orientation='Horizontal' HorizontalAlignment='Right'>
          <Button x:Name='BtnAiRefresh' Content='更新AI分析' Style='{StaticResource PBtn}'/>
          <Button x:Name='BtnAiClose' Content='关闭' Style='{StaticResource PBtnPrimary}'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Border>";

        internal static readonly string Confirm = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource DialogBg}'>
  <Border.Resources>" + BtnStyles + @"    <!-- 确认框的两个本地差异：取消键 = Esc（IsCancel），确认键 = Enter（IsDefault） -->
    <Style x:Key='PBtnCancel' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='IsCancel' Value='True'/>
    </Style>
    <Style x:Key='PBtnYes' TargetType='Button' BasedOn='{StaticResource PBtnPrimary}'>
      <Setter Property='IsCancel' Value='False'/>
      <Setter Property='IsDefault' Value='True'/>
    </Style>
  </Border.Resources>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <TextBlock x:Name='DlgGlyph' Text='!' FontSize='21' FontWeight='SemiBold'
                   Foreground='{DynamicResource Accent}'
                   HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock x:Name='DlgTitle' Text='确认操作' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='DlgSubtitle' Text='此操作会立即生效' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <!-- 失败明细可能很长：限高 + 滚动，避免 SizeToContent=Height 把底部按钮顶出工作区 -->
    <ScrollViewer x:Name='DlgScroll' Grid.Row='1' VerticalScrollBarVisibility='Auto'
                  HorizontalScrollBarVisibility='Disabled' MaxHeight='420'>
      <TextBlock x:Name='DlgText' TextWrapping='Wrap' LineHeight='22' FontSize='12.5'
                 VerticalAlignment='Top'
                 Foreground='{DynamicResource TextSecondary}'/>
    </ScrollViewer>
    <Border Grid.Row='2' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
            Padding='0,14,0,0' Margin='0,16,0,0'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='BtnNo' Content='取消' Style='{StaticResource PBtnCancel}'/>
        <Button x:Name='BtnThird' Content='补全分析' Style='{StaticResource PBtnFlat}' Visibility='Collapsed'/>
        <Button x:Name='BtnYes' Content='确认' Style='{StaticResource PBtnYes}'/>
      </StackPanel>
    </Border>
  </Grid>
</Border>";

        // 批量操作进度：显示当前进度并可取消，避免长时间操作把界面卡死
        internal static readonly string Progress = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Background='{DynamicResource DialogBg}'>
  <Border.Resources>" + BtnStyles + @"    <!-- 进度窗取消按钮独占一行右对齐，去掉按钮间的左外边距 -->
    <Style x:Key='PBtnPg' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Margin' Value='0'/>
    </Style>
  </Border.Resources>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='19' Height='19' Stretch='Uniform' Stroke='{DynamicResource Accent}'
              StrokeThickness='1.6' Fill='Transparent' StrokeLineJoin='Round'
              Data='M 10,2 A 8,8 0 1 1 4.3,4.3 M 10,5.6 L 10,10 L 13,11.8'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock x:Name='PgHead' Text='正在执行操作' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='PgHint' Text='可以随时点击右下角「取消」，已完成的项目会保留' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <TextBlock x:Name='PgText' Grid.Row='1' Text='准备中…' FontSize='12.5' TextWrapping='Wrap'
               Foreground='{DynamicResource TextSecondary}' Margin='0,0,0,10'/>
    <ProgressBar x:Name='PgBar' Grid.Row='2' Height='6' Minimum='0' Maximum='1' Value='0'
                 Background='{DynamicResource BorderColor}' Foreground='{DynamicResource Accent}'>
      <ProgressBar.Template>
        <ControlTemplate TargetType='ProgressBar'>
          <Border Background='{TemplateBinding Background}' CornerRadius='3' ClipToBounds='True'>
            <Grid x:Name='PART_Track'>
              <Border x:Name='PART_Indicator' HorizontalAlignment='Left' CornerRadius='3'
                      Background='{TemplateBinding Foreground}'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ProgressBar.Template>
    </ProgressBar>
    <!-- 本次累计 Token：默认折叠，只有逐项 AI 分析会展开，其它批量操作窗口高度不变 -->
    <TextBlock x:Name='PgTokens' Grid.Row='3' Text='' FontSize='11' Visibility='Collapsed'
               HorizontalAlignment='Right' Margin='0,8,0,0' TextTrimming='CharacterEllipsis'
               Foreground='{DynamicResource TextSecondary}'/>
    <Border Grid.Row='4' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1.34,0,0'
            Padding='0,14,0,0' Margin='0,18,0,0'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='BtnCancel' Content='取消' Style='{StaticResource PBtnPg}'/>
      </StackPanel>
    </Border>
  </Grid>
</Border>";
    }

}
